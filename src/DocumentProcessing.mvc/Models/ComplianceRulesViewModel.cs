using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.mvc.Models;

public sealed class ComplianceRulesViewModel
{
    public IReadOnlyList<ComplianceRuleRecord> Rules { get; init; } = [];
    public string? Error { get; init; }
}
