namespace DocumentProcessing.Infrastructure.Workflow;

/// <summary>
/// Single internal representation of workflow statuses.
/// The string values are the external contract and must stay unchanged.
/// </summary>
public static class WorkflowStatus
{
    public const string Received = "RECEIVED";
    public const string OcrInProgress = "OCR_IN_PROGRESS";
    public const string OcrFailed = "OCR_FAILED";
    public const string OcrSuccess = "OCR_Success";
    public const string ComplianceInProgress = "COMPLIANCE_CHECK_IN_PROGRESS";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Received,
        OcrInProgress,
        OcrFailed,
        OcrSuccess,
        ComplianceInProgress,
        Approved,
        Rejected
    };

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    public static bool IsTerminal(string? status) =>
        status is Approved or Rejected;

    public static bool CanResume(string? status) =>
        status is Received or OcrInProgress or OcrFailed or OcrSuccess or ComplianceInProgress;
}
