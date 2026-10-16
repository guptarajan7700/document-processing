using System.Text.RegularExpressions;
using DocumentProcessing.Infrastructure.Workflow;

namespace DocumentProcessing.Tools.Compliance;

public static partial class DocumentIdentifierExtractor
{
    public static string? Extract(string documentType, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DocumentTypes.Normalize(documentType) switch
        {
            DocumentTypes.Pan => Match(PanPattern(), text.ToUpperInvariant()),
            DocumentTypes.Aadhar => Match(AadharPattern(), text),
            DocumentTypes.Loa => Match(LoaPattern(), text.ToUpperInvariant()),
            _ => null
        };
    }

    private static string? Match(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"\b[A-Z]{5}[0-9]{4}[A-Z]\b", RegexOptions.CultureInvariant)]
    private static partial Regex PanPattern();

    [GeneratedRegex(@"\b[0-9]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex AadharPattern();

    [GeneratedRegex(@"\bLOA-[A-Z0-9-]{4,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LoaPattern();
}
