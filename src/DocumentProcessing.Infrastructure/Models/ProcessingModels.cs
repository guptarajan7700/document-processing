namespace DocumentProcessing.Infrastructure.Models;

public sealed class DocumentRecord
{
    public Guid DocumentId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedOn { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public string? LatestMessage { get; set; }
    public string? StoragePath { get; set; }
    public Guid? LockToken { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class WorkflowHistoryEntry
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

public sealed class WorkflowStatusUpdate
{
    public required Guid DocumentId { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public string? AgentName { get; init; }
    public string? ToolName { get; init; }
    public int? AttemptNumber { get; init; }
    public Guid? CorrelationId { get; init; }
    public int? DurationMs { get; init; }
    public string? ExceptionType { get; init; }
    public bool IsExplicitRetry { get; init; }
}

public sealed class OcrResultRecord
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string? ExtractedIdentifier { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool IsMock { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime CreatedOn { get; set; }
}

public sealed class ComplianceRuleRecord
{
    public int RuleId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string RuleKind { get; set; } = string.Empty;
    public int? ExpectedLength { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public sealed class ComplianceValidationResult
{
    public required bool IsApproved { get; init; }
    public required string Remarks { get; init; }
    public string? ExtractedIdentifier { get; init; }
    public IReadOnlyList<string> Evaluations { get; init; } = [];
    public string Decision => IsApproved ? "APPROVED" : "REJECTED";
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
}

public sealed class DocumentListFilter
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
    public string? DocumentType { get; init; }
    public string? Search { get; init; }
}

public sealed class DocumentStatusCounts
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

public enum WorkflowLockState
{
    Acquired,
    NotFound,
    AlreadyComplete,
    Conflict
}

public sealed class WorkflowLockResult
{
    public required WorkflowLockState State { get; init; }
    public string? CurrentStatus { get; init; }
}
