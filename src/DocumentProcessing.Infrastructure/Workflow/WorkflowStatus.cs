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

    // Role: Checks that the status is one of the seven known values. Next: WorkflowTransitionPolicy.IsAllowed in WorkflowTransitionPolicy.cs.
    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    // Role: Maps a filter value to the exact status string. Next: DocumentsController.List in DocumentsController.cs.
    public static bool TryCanonical(string? status, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var match = All.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        canonical = match;
        return true;
    }

    // Role: True for APPROVED and REJECTED. Next: none. The workflow stops.
    public static bool IsTerminal(string? status) =>
        status is Approved or Rejected;

    // Role: True when Retry is allowed. Next: DocumentsController.Retry in DocumentsController.cs.
    public static bool CanResume(string? status) =>
        status is Received or OcrInProgress or OcrFailed or OcrSuccess or ComplianceInProgress;
}
