using System.Text.RegularExpressions;
using DocumentProcessing.Infrastructure.Workflow;

namespace DocumentProcessing.Tools.Compliance;

public static partial class DocumentIdentifierExtractor
{
    // Role: Finds the PAN, AADHAR, or LOA token in the extracted text. Next: SqlTool.SaveExtractedTextAsync in SqlTool.cs.
    public static string? Extract(string documentType, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DocumentTypes.Normalize(documentType) switch
        {
            DocumentTypes.Pan => Compact(Match(PanPattern(), text.ToUpperInvariant())),
            DocumentTypes.Aadhar => Digits(Match(AadharPattern(), text)),
            DocumentTypes.Loa => Match(LoaPattern(), text.ToUpperInvariant()),
            _ => null
        };
    }

    // Role: Removes spaces that OCR inserts inside a PAN. Next: ComplianceTextValidator.Validate in ComplianceTextValidator.cs.
    private static string? Compact(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var compact = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return compact.Length == 10 ? compact : null;
    }

    // Role: Removes spaces or hyphens that OCR inserts inside an AADHAR number. Next: ComplianceTextValidator.Validate in ComplianceTextValidator.cs.
    private static string? Digits(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 12 ? digits : null;
    }

    private static string? Match(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\b[A-Z]{5}[ \-]?[0-9]{4}[ \-]?[A-Z]\b", RegexOptions.CultureInvariant)]
    private static partial Regex PanPattern();

    [GeneratedRegex(@"\b[0-9](?:[ \-]?[0-9]){11}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AadharPattern();

    [GeneratedRegex(@"\bLOA-[A-Z0-9-]{2,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LoaPattern();
}
