using DocumentProcessing.Agents;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Sql;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Orchestration;

public sealed class CoordinatorAgent(ISqlTool sql, ILogger<CoordinatorAgent> logger)
{
    public const string RouteVirusScan = "virus-scan";
    public const string RouteOcr = "ocr";
    public const string RouteCompliance = "compliance";
    public const string RouteDone = "done";

    // Role: Reads the SQL status and chooses virus-scan, ocr, compliance, or done. Next: RouteReceivedAsync in this file when status is RECEIVED.
    public async Task<string> DecideNextRouteAsync(Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        var document = await sql.GetDocumentAsync(documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        var route = document.CurrentStatus switch
        {
            WorkflowStatus.Received => await RouteReceivedAsync(documentId, cancellationToken),
            WorkflowStatus.OcrInProgress => await RecoverOcrAsync(document, correlationId, cancellationToken),
            WorkflowStatus.OcrFailed => RouteDone,
            WorkflowStatus.OcrSuccess => RouteCompliance,
            WorkflowStatus.ComplianceInProgress => RouteCompliance,
            WorkflowStatus.Approved or WorkflowStatus.Rejected => RouteDone,
            _ => throw new InvalidWorkflowTransitionException($"Status '{document.CurrentStatus}' has no route.")
        };

        logger.LogInformation(
            "Coordinator selected route {Route} for {DocumentId} from status {Status}. CorrelationId={CorrelationId}",
            route,
            documentId,
            document.CurrentStatus,
            correlationId);
        return route;
    }

    // Role: Sends a new document to virus scan, or skips it when the scan already passed. Next: VirusScanAgent.ExecuteAsync in VirusScanAgent.cs.
    private async Task<string> RouteReceivedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        var alreadyPassed = history.Any(entry =>
            entry.AgentName == AgentNames.VirusScan && entry.Message == VirusScanAgent.PassedMessage);
        return alreadyPassed ? RouteOcr : RouteVirusScan;
    }

    // Role: Resumes OCR, or skips it when text is already saved. Next: OcrAgent.ExecuteAsync or ComplianceAgent.ExecuteAsync.
    private async Task<string> RecoverOcrAsync(DocumentRecord document, Guid correlationId, CancellationToken cancellationToken)
    {
        var ocr = await sql.GetLatestOcrResultAsync(document.DocumentId, cancellationToken);
        if (ocr is null)
        {
            logger.LogInformation(
                "Recovery will resume OCR for {DocumentId} because no extracted text is stored",
                document.DocumentId);
            return RouteOcr;
        }

        await sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
        {
            DocumentId = document.DocumentId,
            Status = WorkflowStatus.OcrSuccess,
            Message = "Recovery repaired the status because OCR text was already stored. OCR will not be repeated.",
            AgentName = AgentNames.Coordinator,
            ToolName = ToolNames.UpdateWorkflowStatus,
            CorrelationId = correlationId
        }, cancellationToken);
        return RouteCompliance;
    }
}
