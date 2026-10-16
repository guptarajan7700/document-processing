using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Ocr;

namespace DocumentProcessing.Tools.Compliance;

public static class ComplianceTextValidator
{
    public const string AuthenticityLimitation =
        "Sample length rules do not establish that the document is authentic.";

    public static ComplianceValidationResult Validate(
        string documentType,
        string extractedText,
        IReadOnlyList<ComplianceRuleRecord> rules)
    {
        var type = DocumentTypes.Normalize(documentType);
        var activeRules = rules
            .Where(rule => rule.IsActive && string.Equals(rule.DocumentType, type, StringComparison.Ordinal))
            .ToArray();
        var identifier = DocumentIdentifierExtractor.Extract(type, extractedText);
        var evaluations = new List<string>();

        if (extractedText.Contains(SubmittedFileTextExtractor.ImageUnreadMessage, StringComparison.Ordinal)
            || extractedText.Contains(SubmittedFileTextExtractor.BinaryUnreadMessage, StringComparison.Ordinal))
        {
            evaluations.Add(extractedText.Trim());
        }

        if (identifier is null)
        {
            var otherType = FindOtherDocumentType(type, extractedText);
            evaluations.Add(otherType is null
                ? $"No {type} value was found in the text extracted from the submitted file."
                : $"No {type} value was found in the submitted file. The file contains a {otherType} value.");
        }

        if (activeRules.Length == 0)
        {
            evaluations.Add($"No active compliance rules are configured for {type}.");
            return Reject(identifier, evaluations);
        }

        foreach (var rule in activeRules)
        {
            var actual = identifier?.Length ?? 0;
            var expected = rule.ExpectedLength ?? 0;
            var passed = rule.RuleKind switch
            {
                "ExactLength" => identifier is not null && actual == expected,
                "MinimumLength" => identifier is not null && actual >= expected,
                _ => false
            };

            evaluations.Add(passed
                ? $"{rule.RuleName}: passed ({rule.RuleKind} {expected}, actual {actual})."
                : $"{rule.RuleName}: failed ({rule.RuleKind} expected {expected}, actual {actual}).");
        }

        if (evaluations.Any(item => item.Contains("failed", StringComparison.Ordinal)))
        {
            return Reject(identifier, evaluations);
        }

        return new ComplianceValidationResult
        {
            IsApproved = true,
            ExtractedIdentifier = identifier,
            Evaluations = evaluations,
            Remarks = $"Approved by sample rules. {AuthenticityLimitation}"
        };
    }

    private static string? FindOtherDocumentType(string documentType, string extractedText)
    {
        foreach (var candidate in new[] { DocumentTypes.Pan, DocumentTypes.Aadhar, DocumentTypes.Loa })
        {
            if (candidate == documentType)
            {
                continue;
            }

            if (DocumentIdentifierExtractor.Extract(candidate, extractedText) is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static ComplianceValidationResult Reject(string? identifier, List<string> evaluations) =>
        new()
        {
            IsApproved = false,
            ExtractedIdentifier = identifier,
            Evaluations = evaluations,
            Remarks = $"Rejected. {string.Join(" ", evaluations)} {AuthenticityLimitation}"
        };
}
