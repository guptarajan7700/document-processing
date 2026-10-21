using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Agents.Logging;

public static class AgentLog
{
    // Role: Adds correlation id, document id, and agent name to the log scope. Next: the agent method that called Begin continues.
    public static IDisposable Begin(
        ILogger logger,
        Guid correlationId,
        Guid documentId,
        string agentName,
        string? toolName,
        int? attempt)
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["DocumentId"] = documentId,
            ["AgentName"] = agentName,
            ["ToolName"] = toolName ?? string.Empty,
            ["Attempt"] = attempt ?? 0
        }) ?? NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
