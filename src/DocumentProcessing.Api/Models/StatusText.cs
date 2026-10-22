namespace DocumentProcessing.Api.Models;

public static class StatusText
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
