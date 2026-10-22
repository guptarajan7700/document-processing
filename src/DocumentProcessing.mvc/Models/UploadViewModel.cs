namespace DocumentProcessing.mvc.Models;

public sealed class UploadViewModel
{
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public long MaxUploadBytes { get; init; }
    public string? Error { get; set; }
}
