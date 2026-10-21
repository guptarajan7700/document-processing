using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Tools.Sql;
using Microsoft.Extensions.Caching.Memory;

namespace DocumentProcessing.Tools.Compliance;

public sealed class ComplianceRulesTool(ISqlTool sql, IMemoryCache cache) : IComplianceRulesTool
{
    // Role: Loads active rules, using a 60-second memory cache. Next: SqlTool.GetComplianceRulesAsync in SqlTool.cs on a cache miss.
    public async Task<IReadOnlyList<ComplianceRuleRecord>> GetRulesAsync(string? documentType, CancellationToken cancellationToken)
    {
        var key = "compliance-rules:" + (documentType ?? "*");
        if (cache.TryGetValue(key, out IReadOnlyList<ComplianceRuleRecord>? cached) && cached is not null)
        {
            return cached;
        }

        var rules = await sql.GetComplianceRulesAsync(documentType, activeOnly: true, cancellationToken);
        cache.Set(key, rules, TimeSpan.FromSeconds(60));
        return rules;
    }

    // Role: Loads rules and checks the extracted text. Next: ComplianceTextValidator.Validate in ComplianceTextValidator.cs.
    public async Task<ComplianceValidationResult> ValidateTextAsync(
        string documentType,
        string extractedText,
        CancellationToken cancellationToken)
    {
        var rules = await GetRulesAsync(documentType, cancellationToken);
        return ComplianceTextValidator.Validate(documentType, extractedText, rules);
    }
}
