using DocumentProcessing.Infrastructure.Exceptions;

namespace DocumentProcessing.Infrastructure.Workflow;

/// <summary>
/// Allowed workflow transitions. The SQL function dbo.fn_WorkflowTransitionAllowed mirrors this table.
/// </summary>
public static class WorkflowTransitionPolicy
{
    public static bool IsAllowed(string fromStatus, string toStatus, bool isExplicitRetry)
    {
        if (!WorkflowStatus.IsKnown(fromStatus) || !WorkflowStatus.IsKnown(toStatus))
        {
            return false;
        }

        if (string.Equals(fromStatus, toStatus, StringComparison.Ordinal))
        {
            return true;
        }

        return (fromStatus, toStatus, isExplicitRetry) switch
        {
            (WorkflowStatus.Received, WorkflowStatus.OcrInProgress, _) => true,
            (WorkflowStatus.Received, WorkflowStatus.Rejected, _) => true,
            (WorkflowStatus.OcrInProgress, WorkflowStatus.OcrSuccess, _) => true,
            (WorkflowStatus.OcrInProgress, WorkflowStatus.OcrFailed, _) => true,
            (WorkflowStatus.OcrFailed, WorkflowStatus.OcrInProgress, true) => true,
            (WorkflowStatus.OcrSuccess, WorkflowStatus.ComplianceInProgress, _) => true,
            (WorkflowStatus.ComplianceInProgress, WorkflowStatus.Approved, _) => true,
            (WorkflowStatus.ComplianceInProgress, WorkflowStatus.Rejected, _) => true,
            (WorkflowStatus.ComplianceInProgress, WorkflowStatus.OcrSuccess, _) => true,
            _ => false
        };
    }

    public static void Ensure(string fromStatus, string toStatus, bool isExplicitRetry)
    {
        if (!IsAllowed(fromStatus, toStatus, isExplicitRetry))
        {
            throw new InvalidWorkflowTransitionException(
                $"Transition from '{fromStatus}' to '{toStatus}' is not allowed.");
        }
    }
}
