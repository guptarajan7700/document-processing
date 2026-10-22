using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.mvc.Models;

public sealed class WorkflowHistoryViewModel
{
    public Guid DocumentId { get; init; }
    public IReadOnlyList<WorkflowHistoryEntry> Items { get; init; } = [];
    public string? Error { get; init; }
}
