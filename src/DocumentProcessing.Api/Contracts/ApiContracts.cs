using System.ComponentModel.DataAnnotations;

namespace DocumentProcessing.Api.Contracts;

public sealed class UploadDocumentRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string CustomerId { get; set; } = string.Empty;

    [Required]
    public string DocumentType { get; set; } = string.Empty;

    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    public string FileContentBase64 { get; set; } = string.Empty;
}

public sealed class UploadDocumentResponse
{
    public Guid DocumentId { get; set; }
    public string Status { get; set; } = "RECEIVED";
    public string Message { get; set; } = "Document accepted. Workflow processing continues in the background and can be recovered from SQL if the API stops.";
}

public sealed class DocumentResponse
{
    public Guid DocumentId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedOn { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public string? LatestMessage { get; set; }
}

public sealed class DocumentStatusResponse
{
    public Guid DocumentId { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public DateTime UploadedOn { get; set; }
    public DateTime? LastStatusOn { get; set; }
}

public sealed class DocumentDetailsResponse
{
    public Guid DocumentId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedOn { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public bool CanRetry { get; set; }
    public OcrDetailsResponse? Ocr { get; set; }
    public ComplianceDetailsResponse? Compliance { get; set; }
}

public sealed class OcrDetailsResponse
{
    public string Provider { get; set; } = string.Empty;
    public bool IsMock { get; set; }
    public string? ExtractedIdentifier { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public DateTime CreatedOn { get; set; }
    public string Notice { get; set; } = "Text was read from the submitted file. An image is read with Windows OCR on this PC.";
}

public sealed class ComplianceDetailsResponse
{
    public string? Decision { get; set; }
    public string? Remarks { get; set; }
}

public sealed class WorkflowHistoryResponse
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? AgentName { get; set; }
    public string? ToolName { get; set; }
    public int? AttemptNumber { get; set; }
    public Guid? CorrelationId { get; set; }
    public int? DurationMs { get; set; }
    public string? ExceptionType { get; set; }
}

public sealed class PagedDocumentsResponse
{
    public IReadOnlyList<DocumentResponse> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public sealed class DocumentSummaryResponse
{
    public int Total { get; set; }
    public int Received { get; set; }
    public int OcrInProgress { get; set; }
    public int OcrFailed { get; set; }
    public int OcrSuccess { get; set; }
    public int ComplianceInProgress { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
}

public sealed class ComplianceRuleResponse
{
    public int RuleId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string RuleKind { get; set; } = string.Empty;
    public int? ExpectedLength { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public sealed class WorkflowActionResponse
{
    public Guid DocumentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
