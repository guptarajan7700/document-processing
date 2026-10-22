using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.Api.Models;

public sealed class DocumentDetailsViewModel
{
    public const string OcrNotice = "Text was read from the submitted file. An image is read with Windows OCR on this PC.";

    public DocumentRecord? Document { get; init; }
    public OcrResultRecord? Ocr { get; init; }
    public string? ComplianceDecision { get; init; }
    public string? ComplianceRemarks { get; init; }
    public string? PausedMessage { get; init; }
    public bool CanRetry { get; init; }
    public bool Refresh { get; init; }
    public string? Notice { get; init; }
    public string? Error { get; init; }
}
