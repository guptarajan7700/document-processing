using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.Tools.Compliance;

public interface IComplianceRulesTool
{
    Task<IReadOnlyList<ComplianceRuleRecord>> GetRulesAsync(string? documentType, CancellationToken cancellationToken);

    Task<ComplianceValidationResult> ValidateTextAsync(string documentType, string extractedText, CancellationToken cancellationToken);
}
