using System.Diagnostics;
using DocumentProcessing.Agents.Logging;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Telemetry;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Compliance;
using DocumentProcessing.Tools.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Agents;

public sealed class ComplianceAgent(
    ISqlTool sql,
    IComplianceRulesTool rules,
    IOptions<WorkflowOptions> options,
    ILogger<ComplianceAgent> logger)
{
    public const string CompensationMessage =
        "Compensation: compliance step failed permanently. Status rolled back to OCR_Success so the workflow can resume without repeating OCR.";

    // Role: Checks OCR text against the SQL rules and sets APPROVED or REJECTED. Next: ComplianceRulesTool.GetRulesAsync in ComplianceRulesTool.cs.
    public async Task<AgentStepResult> ExecuteAsync(Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        using var activity = ProcessingTelemetry.Start("ComplianceAgent", documentId, correlationId, AgentNames.Compliance, ToolNames.ValidateText);
        var document = await sql.GetDocumentAsync(documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        if (document.CurrentStatus is not (WorkflowStatus.OcrSuccess or WorkflowStatus.ComplianceInProgress))
        {
            throw new InvalidWorkflowTransitionException(
                "Compliance cannot start before OCR succeeds. Current status is " + document.CurrentStatus + ".");
        }

        if (WorkflowStatus.IsTerminal(document.CurrentStatus))
        {
            return new AgentStepResult
            {
                Succeeded = true,
                Status = document.CurrentStatus,
                Message = "Document is already complete."
            };
        }

        var started = Stopwatch.StartNew();
        using var scope = AgentLog.Begin(logger, correlationId, documentId, AgentNames.Compliance, ToolNames.ValidateText, 1);
        await UpdateAsync(documentId, correlationId, WorkflowStatus.ComplianceInProgress, "Compliance check started.", null, null, null, cancellationToken);

        var maxAttempts = Math.Max(1, options.Value.MaxRetryAttempts);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var attemptWatch = Stopwatch.StartNew();
            try
            {
                var loaded = await rules.GetRulesAsync(document.DocumentType, cancellationToken);
                logger.LogInformation(
                    "Loaded {RuleCount} compliance rules for {DocumentId} type {DocumentType}",
                    loaded.Count,
                    documentId,
                    document.DocumentType);

                var ocr = await sql.GetLatestOcrResultAsync(documentId, cancellationToken);
                if (ocr is null)
                {
                    return await FinishAsync(
                        documentId,
                        correlationId,
                        WorkflowStatus.Rejected,
                        "Rejected. OCR text was not available for compliance validation. " + ComplianceTextValidator.AuthenticityLimitation,
                        attempt,
                        (int)attemptWatch.ElapsedMilliseconds,
                        null,
                        cancellationToken);
                }

                var validation = await rules.ValidateTextAsync(document.DocumentType, ocr.ExtractedText, cancellationToken);
                var status = validation.IsApproved ? WorkflowStatus.Approved : WorkflowStatus.Rejected;
                return await FinishAsync(
                    documentId,
                    correlationId,
                    status,
                    validation.Remarks,
                    attempt,
                    (int)started.ElapsedMilliseconds,
                    null,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is TransientToolException)
            {
                lastError = exception;
                logger.LogWarning(exception, "Transient compliance failure for {DocumentId} on attempt {Attempt}", documentId, attempt);
                await UpdateAsync(
                    documentId,
                    correlationId,
                    WorkflowStatus.ComplianceInProgress,
                    $"Compliance attempt {attempt} failed with a transient error.",
                    attempt,
                    (int)attemptWatch.ElapsedMilliseconds,
                    exception.GetType().Name,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is PermanentToolException)
            {
                return await CompensateAsync(documentId, correlationId, attempt, exception, attemptWatch, cancellationToken);
            }
        }

        return await CompensateAsync(
            documentId,
            correlationId,
            maxAttempts,
            lastError ?? new PermanentToolException("Compliance retries were exhausted."),
            started,
            cancellationToken);
    }

    // Role: Rolls a permanent compliance failure back to OCR_Success. Next: SqlTool.UpdateWorkflowStatusAsync in SqlTool.cs.
    private async Task<AgentStepResult> CompensateAsync(
        Guid documentId,
        Guid correlationId,
        int attempt,
        Exception exception,
        Stopwatch watch,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Permanent compliance failure for {DocumentId}. Rolling status back to OCR_Success", documentId);
        await UpdateAsync(
            documentId,
            correlationId,
            WorkflowStatus.OcrSuccess,
            CompensationMessage + " " + exception.Message,
            attempt,
            (int)watch.ElapsedMilliseconds,
            exception.GetType().Name,
            cancellationToken);
        return new AgentStepResult { Succeeded = false, Status = WorkflowStatus.OcrSuccess, Message = CompensationMessage };
    }

    // Role: Writes the final APPROVED or REJECTED audit. Next: SqlTool.UpdateWorkflowStatusAsync in SqlTool.cs.
    private async Task<AgentStepResult> FinishAsync(
        Guid documentId,
        Guid correlationId,
        string status,
        string message,
        int attempt,
        int durationMs,
        string? exceptionType,
        CancellationToken cancellationToken)
    {
        await UpdateAsync(documentId, correlationId, status, message, attempt, durationMs, exceptionType, cancellationToken);
        logger.LogInformation(
            "Compliance finished for {DocumentId} with {Status} in {DurationMs} ms",
            documentId,
            status,
            durationMs);
        return new AgentStepResult { Succeeded = status == WorkflowStatus.Approved, Status = status, Message = message };
    }

    private Task UpdateAsync(
        Guid documentId,
        Guid correlationId,
        string status,
        string message,
        int? attempt,
        int? durationMs,
        string? exceptionType,
        CancellationToken cancellationToken) =>
        UpdateAsync(documentId, correlationId, status, message, attempt, durationMs, exceptionType, cancellationToken, explicitRetry: false);

    private Task UpdateAsync(
        Guid documentId,
        Guid correlationId,
        string status,
        string message,
        int? attempt,
        int? durationMs,
        string? exceptionType,
        CancellationToken cancellationToken,
        bool explicitRetry) =>
        sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
        {
            DocumentId = documentId,
            Status = status,
            Message = message,
            AgentName = AgentNames.Compliance,
            ToolName = status == WorkflowStatus.ComplianceInProgress && message.StartsWith("Loaded", StringComparison.Ordinal)
                ? ToolNames.GetRules
                : ToolNames.ValidateText,
            AttemptNumber = attempt,
            CorrelationId = correlationId,
            DurationMs = durationMs,
            ExceptionType = exceptionType,
            IsExplicitRetry = explicitRetry
        }, cancellationToken);
}
