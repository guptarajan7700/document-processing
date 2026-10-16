using System.Diagnostics;
using DocumentProcessing.Agents.Logging;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Storage;
using DocumentProcessing.Infrastructure.Telemetry;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Sql;
using DocumentProcessing.Tools.VirusScan;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Agents;

public sealed class VirusScanAgent(
    ISqlTool sql,
    IDocumentFileStore files,
    IVirusScanTool virusScan,
    ILogger<VirusScanAgent> logger)
{
    public const string PassedMessage =
        "Virus scan passed. This is a mock signature check, not a real antivirus engine.";

    public async Task<AgentStepResult> ExecuteAsync(Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        using var activity = ProcessingTelemetry.Start("VirusScanAgent", documentId, correlationId, AgentNames.VirusScan, ToolNames.VirusScan);
        var started = Stopwatch.StartNew();
        using var scope = AgentLog.Begin(logger, correlationId, documentId, AgentNames.VirusScan, ToolNames.VirusScan, 1);

        var document = await sql.GetDocumentAsync(documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);
        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        if (history.Any(entry => entry.AgentName == AgentNames.VirusScan && entry.Message == PassedMessage))
        {
            return new AgentStepResult
            {
                Succeeded = true,
                Status = document.CurrentStatus,
                Message = PassedMessage
            };
        }

        var content = await files.ReadAsync(documentId, document.FileName, cancellationToken);
        var scan = await virusScan.ScanAsync(document.FileName, content, cancellationToken);
        if (!scan.IsClean)
        {
            logger.LogWarning(
                "VirusScanAgent rejected {DocumentId}. DurationMs={DurationMs}",
                documentId,
                started.ElapsedMilliseconds);
            await sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
            {
                DocumentId = documentId,
                Status = WorkflowStatus.Rejected,
                Message = scan.Message,
                AgentName = AgentNames.VirusScan,
                ToolName = ToolNames.VirusScan,
                CorrelationId = correlationId,
                DurationMs = (int)started.ElapsedMilliseconds
            }, cancellationToken);
            activity?.SetTag("scan.clean", false);
            return new AgentStepResult { Succeeded = false, Status = WorkflowStatus.Rejected, Message = scan.Message };
        }

        await sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
        {
            DocumentId = documentId,
            Status = WorkflowStatus.Received,
            Message = PassedMessage,
            AgentName = AgentNames.VirusScan,
            ToolName = ToolNames.VirusScan,
            CorrelationId = correlationId,
            DurationMs = (int)started.ElapsedMilliseconds
        }, cancellationToken);
        activity?.SetTag("scan.clean", true);
        logger.LogInformation("VirusScanAgent passed {DocumentId} in {DurationMs} ms", documentId, started.ElapsedMilliseconds);
        return new AgentStepResult { Succeeded = true, Status = WorkflowStatus.Received, Message = PassedMessage };
    }
}
