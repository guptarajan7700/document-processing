using DocumentProcessing.Agents;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.Tools.Sql;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Orchestration;

public sealed record WorkflowMessage(Guid DocumentId, Guid CorrelationId, string Route, string? Detail = null);

public static class WorkflowCheckpoints
{
    public const string BeforeCompliance = "BeforeCompliance";
    public const string InterruptMessagePrefix = "Interrupted before compliance.";
}

public interface IWorkflowCheckpointGate
{
    Task OnCheckpointAsync(string checkpoint, Guid documentId, Guid correlationId, CancellationToken cancellationToken);
}

public sealed class ScenarioCheckpointGate(ISqlTool sql, IOcrScenarioSelector scenarios, ILogger<ScenarioCheckpointGate> logger) : IWorkflowCheckpointGate
{
    public async Task OnCheckpointAsync(string checkpoint, Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        if (!string.Equals(checkpoint, WorkflowCheckpoints.BeforeCompliance, StringComparison.Ordinal))
        {
            return;
        }

        var document = await sql.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        if (scenarios.Resolve(documentId, document.FileName) != OcrScenario.SuccessThenInterruptBeforeCompliance)
        {
            return;
        }

        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        if (history.Any(entry => entry.Message.StartsWith(WorkflowCheckpoints.InterruptMessagePrefix, StringComparison.Ordinal)))
        {
            return;
        }

        var message = WorkflowCheckpoints.InterruptMessagePrefix + " OCR output was kept so replay can start at compliance.";
        await sql.UpdateWorkflowStatusAsync(new Infrastructure.Models.WorkflowStatusUpdate
        {
            DocumentId = documentId,
            Status = WorkflowStatus.OcrSuccess,
            Message = message,
            AgentName = AgentNames.Coordinator,
            ToolName = "WorkflowCheckpointGate",
            CorrelationId = correlationId
        }, cancellationToken);

        logger.LogWarning("Workflow interrupted after OCR for {DocumentId}. CorrelationId={CorrelationId}", documentId, correlationId);
        throw new WorkflowInterruptedException(message);
    }
}

public sealed class WorkflowExecutionResult
{
    public required Guid DocumentId { get; init; }
    public required string Status { get; init; }
    public required string Outcome { get; init; }
    public required string Message { get; init; }
}
