using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.Api.Models;

public sealed class DocumentListViewModel
{
    public string Search { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public int TotalCount { get; init; }
    public IReadOnlyList<DocumentRecord> Items { get; init; } = [];
    public string? Error { get; init; }
}
