using System.Threading.Channels;
using DocumentProcessing.Agents;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Orchestration;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.mvc.Hosting;

public sealed class WorkflowWorkQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    // Role: Puts the document id on the in-memory queue. Next: WorkflowRecoveryWorker.ExecuteOneAsync in this file.
    public void Enqueue(Guid documentId) => _channel.Writer.TryWrite(documentId);
}

public sealed class ChannelWorkflowHandoff(WorkflowWorkQueue queue) : IWorkflowHandoff
{
    // Role: Hands the new document to the background worker. Next: WorkflowWorkQueue.Enqueue in this file.
    public void Handoff(Guid documentId) => queue.Enqueue(documentId);
}

public sealed class WorkflowRecoveryWorker(
    WorkflowWorkQueue queue,
    IServiceScopeFactory scopes,
    IOptions<WorkflowOptions> options,
    ILogger<WorkflowRecoveryWorker> logger) : BackgroundService
{
    // Role: Reads the queue and also polls SQL for unfinished documents. Next: ExecuteOneAsync in this file.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableRecoveryHost)
        {
            logger.LogInformation("Workflow recovery host is disabled.");
            return;
        }

        await RecoverAsync(stoppingToken);
        var poll = TimeSpan.FromSeconds(Math.Max(5, options.Value.RecoveryPollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                pollCts.CancelAfter(poll);
                while (await queue.Reader.WaitToReadAsync(pollCts.Token))
                {
                    while (queue.Reader.TryRead(out var documentId))
                    {
                        await ExecuteOneAsync(documentId, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                await RecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow recovery loop failed. ExceptionType={ExceptionType}", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    // Role: Finds unlocked documents that are not finished. Next: WorkflowWorkQueue.Enqueue in this file.
    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var sql = scope.ServiceProvider.GetRequiredService<DocumentProcessing.Tools.Sql.ISqlTool>();
        var pending = await sql.GetRecoverableDocumentsAsync(cancellationToken);
        foreach (var document in pending)
        {
            if (document.CurrentStatus == DocumentProcessing.Infrastructure.Workflow.WorkflowStatus.OcrSuccess)
            {
                var history = await sql.GetHistoryAsync(document.DocumentId, cancellationToken);
                var latest = history.LastOrDefault();
                if (latest?.Message.StartsWith(ComplianceAgent.CompensationMessage, StringComparison.Ordinal) == true)
                {
                    continue;
                }
            }

            queue.Enqueue(document.DocumentId);
        }

        if (pending.Count > 0)
        {
            logger.LogInformation("Recovery queued {Count} unfinished documents from SQL", pending.Count);
        }
    }

    // Role: Starts one workflow on a background thread. Next: DocumentWorkflowService.ExecuteAsync in DocumentWorkflowRunner.cs.
    private async Task ExecuteOneAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        using var scope = scopes.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<DocumentWorkflowService>();
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["DocumentId"] = documentId,
            ["AgentName"] = "WorkflowRecoveryWorker"
        });

        try
        {
            var result = await workflow.ExecuteAsync(documentId, correlationId, cancellationToken);
            logger.LogInformation(
                "Background workflow outcome {Outcome} status {Status} for {DocumentId}",
                result.Outcome,
                result.Status,
                documentId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Background workflow failed for {DocumentId}. ExceptionType={ExceptionType}. SQL state is retained for recovery.",
                documentId,
                exception.GetType().Name);
        }
    }
}
