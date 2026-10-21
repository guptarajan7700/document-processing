using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.Api.Ui;

public static class UiText
{
    public static string StatusLabel(string? status) => status switch
    {
        "RECEIVED" => "Received",
        "OCR_IN_PROGRESS" => "OCR in progress",
        "OCR_FAILED" => "OCR failed",
        "OCR_Success" => "OCR success",
        "COMPLIANCE_CHECK_IN_PROGRESS" => "Compliance in progress",
        "APPROVED" => "Approved",
        "REJECTED" => "Rejected",
        _ => status ?? string.Empty
    };

    public static string BadgeClass(string? status) => status switch
    {
        "APPROVED" or "OCR_Success" => "success",
        "OCR_FAILED" or "REJECTED" => "failed",
        "OCR_IN_PROGRESS" => "progress",
        "COMPLIANCE_CHECK_IN_PROGRESS" => "compliance",
        "RECEIVED" => "received",
        _ => string.Empty
    };

    public static bool IsWorking(string? status) =>
        status is not ("APPROVED" or "REJECTED" or "OCR_FAILED");
}

public sealed class DashboardPage
{
    public DocumentStatusCounts Counts { get; init; } = new();
    public IReadOnlyList<WorkflowHistoryEntry> Activity { get; init; } = [];
    public string? Error { get; init; }
}

public sealed class DocumentsPage
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

public sealed class UploadPage
{
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public long MaxUploadBytes { get; init; }
    public string? Error { get; set; }
}

public sealed class DetailsPage
{
    public const string OcrNotice = "Text was read from the submitted file. An image is read with Windows OCR on this PC.";

    public DocumentRecord? Document { get; init; }
    public OcrResultRecord? Ocr { get; init; }
    public string? ComplianceDecision { get; init; }
    public string? ComplianceRemarks { get; init; }
    public bool CanRetry { get; init; }
    public bool Refresh { get; init; }
    public string? Notice { get; init; }
    public string? Error { get; init; }
}

public sealed class HistoryPage
{
    public Guid DocumentId { get; init; }
    public IReadOnlyList<WorkflowHistoryEntry> Items { get; init; } = [];
    public string? Error { get; init; }
}

public sealed class RulesPage
{
    public IReadOnlyList<ComplianceRuleRecord> Rules { get; init; } = [];
    public string? Error { get; init; }
}
