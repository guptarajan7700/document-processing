using DocumentProcessing.mvc.Contracts;
using DocumentProcessing.Tools.Compliance;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.mvc.Controllers.Api;

[ApiController]
[Route("api/compliance-rules")]
public sealed class ComplianceRulesController(IComplianceRulesTool rules) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ComplianceRuleResponse>), StatusCodes.Status200OK)]
    // Role: Returns the compliance rules for the rules page. Next: ComplianceRulesTool.GetRulesAsync in ComplianceRulesTool.cs.
    public async Task<ActionResult<IReadOnlyList<ComplianceRuleResponse>>> Get([FromQuery] string? documentType, CancellationToken cancellationToken)
    {
        var rows = await rules.GetRulesAsync(string.IsNullOrWhiteSpace(documentType) ? null : documentType.Trim().ToUpperInvariant(), cancellationToken);
        return Ok(rows.Select(rule => new ComplianceRuleResponse
        {
            RuleId = rule.RuleId,
            DocumentType = rule.DocumentType,
            RuleName = rule.RuleName,
            RuleKind = rule.RuleKind,
            ExpectedLength = rule.ExpectedLength,
            IsActive = rule.IsActive,
            Description = rule.Description
        }).ToArray());
    }
}
