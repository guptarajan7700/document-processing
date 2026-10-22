using DocumentProcessing.mvc.Models;
using DocumentProcessing.Tools.Sql;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.mvc.Controllers;

public sealed class HomeController(ISqlTool sql, ILogger<HomeController> logger) : Controller
{
    // Role: Shows status counts and recent audit rows. Next: SqlTool.GetStatusCountsAsync in SqlTool.cs.
    [HttpGet("/")]
    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var counts = await sql.GetStatusCountsAsync(cancellationToken);
            var activity = await sql.GetRecentActivityAsync(8, cancellationToken);
            return View(new DashboardViewModel { Counts = counts, Activity = activity });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dashboard counts could not be loaded");
            return View(new DashboardViewModel { Error = "Dashboard counts could not be loaded. Check that SQL Server LocalDB is running." });
        }
    }
}
