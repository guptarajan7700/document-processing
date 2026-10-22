using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.mvc.Models;

public sealed class DashboardViewModel
{
    public DocumentStatusCounts Counts { get; init; } = new();
    public IReadOnlyList<WorkflowHistoryEntry> Activity { get; init; } = [];
    public string? Error { get; init; }
}
