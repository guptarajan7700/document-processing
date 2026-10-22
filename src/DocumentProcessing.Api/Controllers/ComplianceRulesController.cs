using DocumentProcessing.Api.Models;
using DocumentProcessing.Tools.Compliance;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.Api.Controllers;

public sealed class ComplianceRulesController(IComplianceRulesTool rules, ILogger<ComplianceRulesController> logger) : Controller
{
    // Role: Shows the SQL compliance rules. Next: ComplianceRulesTool.GetRulesAsync in ComplianceRulesTool.cs.
    [HttpGet("/compliance-rules")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await rules.GetRulesAsync(null, cancellationToken);
            return View(new ComplianceRulesViewModel { Rules = rows });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Compliance rules could not be loaded");
            return View(new ComplianceRulesViewModel { Error = "Compliance rules could not be loaded." });
        }
    }
}
