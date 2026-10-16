using System.Diagnostics;
using DocumentProcessing.Agents.Logging;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Storage;
using DocumentProcessing.Infrastructure.Telemetry;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Compliance;
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.Tools.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Agents;

public sealed class OcrAgent(
    ISqlTool sql,
    IDocumentFileStore files,
    IOcrTool ocr,
    IOptions<WorkflowOptions> options,
    ILogger<OcrAgent> logger)
{
    public const string CompensationMessage =
        "Compensation: OCR failed permanently. Document metadata, stored file, and audit history were retained. Compliance was not started.";

    public async Task<AgentStepResult> ExecuteAsync(Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        using var activity = ProcessingTelemetry.Start("OcrAgent", documentId, correlationId, AgentNames.Ocr, ToolNames.ExtractText);
        var document = await sql.GetDocumentAsync(documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        if (document.CurrentStatus == WorkflowStatus.OcrSuccess)
        {
            return new AgentStepResult
            {
                Succeeded = true,
                Status = WorkflowStatus.OcrSuccess,
                Message = "OCR already succeeded. Extraction was not repeated."
            };
        }

        var started = Stopwatch.StartNew();
        await UpdateAsync(documentId, correlationId, WorkflowStatus.OcrInProgress, "OCR started.", null, null, null, cancellationToken);

        byte[] content;
        try
        {
            content = await files.ReadAsync(documentId, document.FileName, cancellationToken);
        }
        catch (Exception exception) when (exception is PermanentToolException or FileNotFoundException)
        {
            return await FailPermanentAsync(documentId, correlationId, 1, exception, started, cancellationToken);
        }

        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        var usedAttempts = history
            .Where(entry => entry.AgentName == AgentNames.Ocr && entry.AttemptNumber.HasValue && entry.ExceptionType is not null)
            .Select(entry => entry.AttemptNumber!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var maxAttempts = Math.Max(1, options.Value.MaxRetryAttempts);
        Exception? lastError = null;
        for (var attempt = usedAttempts + 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptWatch = Stopwatch.StartNew();
            using var scope = AgentLog.Begin(logger, correlationId, documentId, AgentNames.Ocr, ToolNames.ExtractText, attempt);
            try
            {
                var extraction = await ocr.ExtractTextAsync(new OcrExtractionRequest
                {
                    DocumentId = documentId,
                    DocumentType = document.DocumentType,
                    FileName = document.FileName,
                    Content = content,
                    AttemptNumber = attempt
                }, cancellationToken);

                var identifier = DocumentIdentifierExtractor.Extract(document.DocumentType, extraction.Text);
                await sql.SaveExtractedTextAsync(new OcrResultRecord
                {
                    DocumentId = documentId,
                    ExtractedText = extraction.Text,
                    ExtractedIdentifier = identifier,
                    Provider = extraction.Provider,
                    IsMock = extraction.IsMock,
                    AttemptNumber = attempt
                }, cancellationToken);

                var message = extraction.IsMock
                    ? $"OCR read the submitted file with provider '{extraction.Provider}'. Image pixels are not scanned."
                    : $"OCR completed with provider '{extraction.Provider}'.";
                await UpdateAsync(documentId, correlationId, WorkflowStatus.OcrSuccess, message, attempt, (int)attemptWatch.ElapsedMilliseconds, null, cancellationToken);
                logger.LogInformation(
                    "OCR succeeded for {DocumentId} on attempt {Attempt} in {DurationMs} ms",
                    documentId,
                    attempt,
                    attemptWatch.ElapsedMilliseconds);
                return new AgentStepResult { Succeeded = true, Status = WorkflowStatus.OcrSuccess, Message = message };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is TransientToolException)
            {
                lastError = exception;
                logger.LogWarning(
                    exception,
                    "Transient OCR failure for {DocumentId} on attempt {Attempt}. ExceptionType={ExceptionType}",
                    documentId,
                    attempt,
                    exception.GetType().Name);
                await UpdateAsync(
                    documentId,
                    correlationId,
                    WorkflowStatus.OcrInProgress,
                    $"OCR attempt {attempt} failed with a transient error and will be retried if attempts remain.",
                    attempt,
                    (int)attemptWatch.ElapsedMilliseconds,
                    exception.GetType().Name,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is PermanentToolException)
            {
                return await FailPermanentAsync(documentId, correlationId, attempt, exception, attemptWatch, cancellationToken);
            }
        }

        return await FailPermanentAsync(
            documentId,
            correlationId,
            maxAttempts,
            lastError ?? new PermanentToolException("OCR retries were exhausted."),
            started,
            cancellationToken);
    }

    private async Task<AgentStepResult> FailPermanentAsync(
        Guid documentId,
        Guid correlationId,
        int attempt,
        Exception exception,
        Stopwatch watch,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Permanent OCR failure for {DocumentId} on attempt {Attempt}. ExceptionType={ExceptionType} DurationMs={DurationMs}",
            documentId,
            attempt,
            exception.GetType().Name,
            watch.ElapsedMilliseconds);

        await UpdateAsync(
            documentId,
            correlationId,
            WorkflowStatus.OcrFailed,
            $"OCR failed permanently: {exception.Message}",
            attempt,
            (int)watch.ElapsedMilliseconds,
            exception.GetType().Name,
            cancellationToken);
        await UpdateAsync(
            documentId,
            correlationId,
            WorkflowStatus.OcrFailed,
            CompensationMessage,
            attempt,
            null,
            null,
            cancellationToken);

        return new AgentStepResult
        {
            Succeeded = false,
            Status = WorkflowStatus.OcrFailed,
            Message = CompensationMessage
        };
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
        sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
        {
            DocumentId = documentId,
            Status = status,
            Message = message,
            AgentName = AgentNames.Ocr,
            ToolName = ToolNames.UpdateWorkflowStatus,
            AttemptNumber = attempt,
            CorrelationId = correlationId,
            DurationMs = durationMs,
            ExceptionType = exceptionType
        }, cancellationToken);
}
