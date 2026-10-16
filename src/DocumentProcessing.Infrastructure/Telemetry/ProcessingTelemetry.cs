using System.Diagnostics;

namespace DocumentProcessing.Infrastructure.Telemetry;

public static class ProcessingTelemetry
{
    public const string SourceName = "DocumentProcessing";

    public static readonly ActivitySource Source = new(SourceName);

    public static Activity? Start(string name, Guid documentId, Guid correlationId, string? agent = null, string? tool = null)
    {
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        activity?.SetTag("document.id", documentId.ToString());
        activity?.SetTag("correlation.id", correlationId.ToString());
        if (agent is not null)
        {
            activity?.SetTag("agent.name", agent);
        }

        if (tool is not null)
        {
            activity?.SetTag("tool.name", tool);
        }

        return activity;
    }
}
