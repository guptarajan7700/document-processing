namespace DocumentProcessing.Infrastructure.Workflow;

public static class DocumentTypes
{
    public const string Pan = "PAN";
    public const string Aadhar = "AADHAR";
    public const string Loa = "LOA";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Pan,
        Aadhar,
        Loa
    };

    public static bool IsKnown(string? documentType) =>
        documentType is not null && All.Contains(documentType);

    public static string Normalize(string documentType) => documentType.Trim().ToUpperInvariant();
}
