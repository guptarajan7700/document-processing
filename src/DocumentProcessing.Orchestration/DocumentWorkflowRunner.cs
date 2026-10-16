using System.Diagnostics;
using DocumentProcessing.Agents;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Sql;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Orchestration;

public sealed class DocumentWorkflowService(
    ISqlTool sql,
    CoordinatorAgent coordinator,
    VirusScanAgent virusScanAgent,
    OcrAgent ocrAgent,
    ComplianceAgent complianceAgent,
    IWorkflowCheckpointGate checkpointGate,
    IOptions<WorkflowOptions> options,
    ILogger<DocumentWorkflowService> logger)
{
    public async Task<WorkflowExecutionResult> ExecuteAsync(Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        var lockToken = Guid.NewGuid();
        var lease = TimeSpan.FromSeconds(Math.Max(30, options.Value.LockLeaseSeconds));
        var lockResult = await sql.TryAcquireLockAsync(documentId, lockToken, lease, cancellationToken);
        if (lockResult.State == WorkflowLockState.NotFound)
        {
            throw new DocumentNotFoundException(documentId);
        }

        if (lockResult.State == WorkflowLockState.AlreadyComplete)
        {
            return new WorkflowExecutionResult
            {
                DocumentId = documentId,
                Status = lockResult.CurrentStatus ?? WorkflowStatus.Approved,
                Outcome = "AlreadyComplete",
                Message = "Document is already complete. Duplicate processing was skipped."
            };
        }

        if (lockResult.State == WorkflowLockState.Conflict)
        {
            return new WorkflowExecutionResult
            {
                DocumentId = documentId,
                Status = lockResult.CurrentStatus ?? string.Empty,
                Outcome = "Duplicate",
                Message = "Document is already being processed."
            };
        }

        var started = Stopwatch.StartNew();
        try
        {
            var result = await RunFrameworkWorkflowAsync(documentId, correlationId, cancellationToken);
            logger.LogInformation(
                "Workflow execution finished for {DocumentId} with {Status} in {DurationMs} ms. CorrelationId={CorrelationId} Outcome={Outcome}",
                documentId,
                result.Status,
                started.ElapsedMilliseconds,
                correlationId,
                result.Outcome);
            return result;
        }
        catch (Exception exception) when (Find<WorkflowInterruptedException>(exception) is not null)
        {
            var document = await sql.GetDocumentAsync(documentId, CancellationToken.None);
            logger.LogWarning(
                exception,
                "Workflow interrupted for {DocumentId} after {DurationMs} ms. CorrelationId={CorrelationId}",
                documentId,
                started.ElapsedMilliseconds,
                correlationId);
            return new WorkflowExecutionResult
            {
                DocumentId = documentId,
                Status = document?.CurrentStatus ?? WorkflowStatus.OcrSuccess,
                Outcome = "Interrupted",
                Message = Find<WorkflowInterruptedException>(exception)!.Message
            };
        }
        finally
        {
            await sql.ReleaseLockAsync(documentId, lockToken, CancellationToken.None);
        }
    }

    public async Task<WorkflowExecutionResult> RetryAsync(Guid documentId, Guid correlationId, CancellationToken cancellationToken)
    {
        var document = await sql.GetDocumentAsync(documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        if (WorkflowStatus.IsTerminal(document.CurrentStatus) || !WorkflowStatus.CanResume(document.CurrentStatus))
        {
            throw new InvalidWorkflowTransitionException(
                $"Status '{document.CurrentStatus}' is not eligible for retry or resume.");
        }

        if (document.CurrentStatus == WorkflowStatus.OcrFailed)
        {
            await sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
            {
                DocumentId = documentId,
                Status = WorkflowStatus.OcrInProgress,
                Message = "Explicit retry requested. OCR will run again. Compliance still waits for OCR success.",
                AgentName = AgentNames.Coordinator,
                ToolName = ToolNames.UpdateWorkflowStatus,
                CorrelationId = correlationId,
                IsExplicitRetry = true
            }, cancellationToken);
        }

        return await ExecuteAsync(documentId, correlationId, cancellationToken);
    }

    private async Task<WorkflowExecutionResult> RunFrameworkWorkflowAsync(
        Guid documentId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var coordinatorExecutor = new CoordinatorExecutor(coordinator);
        var virusExecutor = new VirusScanWorkflowExecutor(virusScanAgent);
        var ocrExecutor = new OcrWorkflowExecutor(ocrAgent, checkpointGate);
        var complianceExecutor = new ComplianceWorkflowExecutor(complianceAgent);
        var done = new CompleteWorkflowExecutor(sql);

        var builder = new WorkflowBuilder(coordinatorExecutor);
        builder.AddEdge<WorkflowMessage>(coordinatorExecutor, virusExecutor, message => message?.Route == CoordinatorAgent.RouteVirusScan);
        builder.AddEdge<WorkflowMessage>(virusExecutor, ocrExecutor, message => message?.Route == CoordinatorAgent.RouteOcr);
        builder.AddEdge<WorkflowMessage>(virusExecutor, done, message => message?.Route == CoordinatorAgent.RouteDone);
        builder.AddEdge<WorkflowMessage>(coordinatorExecutor, ocrExecutor, message => message?.Route == CoordinatorAgent.RouteOcr);
        builder.AddEdge<WorkflowMessage>(coordinatorExecutor, complianceExecutor, message => message?.Route == CoordinatorAgent.RouteCompliance);
        builder.AddEdge<WorkflowMessage>(coordinatorExecutor, done, message => message?.Route == CoordinatorAgent.RouteDone);
        builder.AddEdge<WorkflowMessage>(ocrExecutor, complianceExecutor, message => message?.Route == CoordinatorAgent.RouteCompliance);
        builder.AddEdge<WorkflowMessage>(ocrExecutor, done, message => message?.Route is CoordinatorAgent.RouteDone or "interrupted");
        builder.AddEdge(complianceExecutor, done);
        builder.WithOutputFrom(done);
        var workflow = builder.Build();

        await using var run = await InProcessExecution.RunAsync(
            workflow,
            new WorkflowMessage(documentId, correlationId, "start"),
            cancellationToken: cancellationToken);

        var output = run.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.As<WorkflowMessage>())
            .LastOrDefault(message => message is not null);

        var document = await sql.GetDocumentAsync(documentId, cancellationToken);
        var interrupted = output?.Route == "interrupted";
        return new WorkflowExecutionResult
        {
            DocumentId = documentId,
            Status = document?.CurrentStatus ?? output?.Route ?? string.Empty,
            Outcome = interrupted ? "Interrupted" : "Completed",
            Message = output?.Detail ?? document?.CurrentStatus ?? "Workflow finished."
        };
    }

    private static TException? Find<TException>(Exception exception) where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }

    private sealed class CoordinatorExecutor(CoordinatorAgent agent) : Executor<WorkflowMessage, WorkflowMessage>("coordinator-agent")
    {
        public override async ValueTask<WorkflowMessage> HandleAsync(
            WorkflowMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var route = await agent.DecideNextRouteAsync(message.DocumentId, message.CorrelationId, cancellationToken);
            return message with { Route = route };
        }
    }

    private sealed class VirusScanWorkflowExecutor(VirusScanAgent agent) : Executor<WorkflowMessage, WorkflowMessage>("virus-scan-agent")
    {
        public override async ValueTask<WorkflowMessage> HandleAsync(
            WorkflowMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var result = await agent.ExecuteAsync(message.DocumentId, message.CorrelationId, cancellationToken);
            var route = result.Succeeded ? CoordinatorAgent.RouteOcr : CoordinatorAgent.RouteDone;
            return message with { Route = route, Detail = result.Message };
        }
    }

    private sealed class OcrWorkflowExecutor(OcrAgent agent, IWorkflowCheckpointGate gate) : Executor<WorkflowMessage, WorkflowMessage>("ocr-agent")
    {
        public override async ValueTask<WorkflowMessage> HandleAsync(
            WorkflowMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var result = await agent.ExecuteAsync(message.DocumentId, message.CorrelationId, cancellationToken);
            if (!result.Succeeded)
            {
                return message with { Route = CoordinatorAgent.RouteDone, Detail = result.Message };
            }

            try
            {
                await gate.OnCheckpointAsync(WorkflowCheckpoints.BeforeCompliance, message.DocumentId, message.CorrelationId, cancellationToken);
            }
            catch (WorkflowInterruptedException exception)
            {
                return message with { Route = "interrupted", Detail = exception.Message };
            }

            return message with { Route = CoordinatorAgent.RouteCompliance, Detail = result.Message };
        }
    }

    private sealed class ComplianceWorkflowExecutor(ComplianceAgent agent) : Executor<WorkflowMessage, WorkflowMessage>("compliance-agent")
    {
        public override async ValueTask<WorkflowMessage> HandleAsync(
            WorkflowMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var result = await agent.ExecuteAsync(message.DocumentId, message.CorrelationId, cancellationToken);
            return message with { Route = CoordinatorAgent.RouteDone, Detail = result.Message };
        }
    }

    private sealed class CompleteWorkflowExecutor(ISqlTool sqlTool) : Executor<WorkflowMessage, WorkflowMessage>("workflow-complete")
    {
        public override async ValueTask<WorkflowMessage> HandleAsync(
            WorkflowMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            if (message.Route == "interrupted")
            {
                return message;
            }

            var document = await sqlTool.GetDocumentAsync(message.DocumentId, cancellationToken);
            return message with
            {
                Route = document?.CurrentStatus ?? message.Route,
                Detail = message.Detail ?? document?.CurrentStatus
            };
        }
    }
}
