using DocumentProcessing.Agents;
using DocumentProcessing.Api.Hosting;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Orchestration;
using DocumentProcessing.Tools.Compliance;
using DocumentProcessing.Tools.Sql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Api.Ui;

public sealed class UiController(
    DocumentIntakeAgent intake,
    DocumentWorkflowService workflows,
    ISqlTool sql,
    IComplianceRulesTool rules,
    IOptions<WorkflowOptions> options) : Controller
{
    // Role: Shows status counts and recent audit rows. Next: SqlTool.GetStatusCountsAsync in SqlTool.cs.
    [HttpGet("/")]
    [HttpGet("/dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        try
        {
            var counts = await sql.GetStatusCountsAsync(cancellationToken);
            var activity = await sql.GetRecentActivityAsync(8, cancellationToken);
            return View(new DashboardPage { Counts = counts, Activity = activity });
        }
        catch (Exception)
        {
            return View(new DashboardPage { Error = "Dashboard counts could not be loaded. Check that SQL Server LocalDB is running." });
        }
    }

    // Role: Shows the upload form. Next: UploadPost in this file when the form is submitted.
    [HttpGet("/upload")]
    public IActionResult Upload() => View(new UploadPage { MaxUploadBytes = options.Value.MaxUploadBytes });

    // Role: Saves the chosen file through the same intake agent as the API. Next: DocumentIntakeAgent.AcceptAsync in DocumentIntakeAgent.cs.
    [HttpPost("/upload")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPost(string customerId, string documentType, IFormFile? file, CancellationToken cancellationToken)
    {
        var page = new UploadPage
        {
            CustomerId = customerId?.Trim() ?? string.Empty,
            DocumentType = documentType ?? string.Empty,
            MaxUploadBytes = options.Value.MaxUploadBytes
        };

        if (file is null || file.Length == 0)
        {
            page.Error = "Choose a file.";
            return View("Upload", page);
        }

        if (file.Length > page.MaxUploadBytes)
        {
            page.Error = $"File exceeds the {page.MaxUploadBytes} byte limit.";
            return View("Upload", page);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var documentId = await intake.AcceptAsync(new DocumentIntakeRequest
            {
                CustomerId = page.CustomerId,
                DocumentType = page.DocumentType,
                FileName = Path.GetFileName(file.FileName),
                FileContentBase64 = Convert.ToBase64String(buffer.ToArray()),
                CorrelationId = CorrelationId
            }, cancellationToken);

            TempData["Notice"] = "Upload accepted. Validation is running.";
            return RedirectToAction(nameof(Details), new { documentId });
        }
        catch (DocumentValidationException exception)
        {
            page.Error = exception.Message;
            return View("Upload", page);
        }
    }

    // Role: Shows the filtered document list. Next: SqlTool.ListDocumentsAsync in SqlTool.cs.
    [HttpGet("/documents")]
    public async Task<IActionResult> Documents(string? search, string? status, string? documentType, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = new DocumentsPage
        {
            Search = search?.Trim() ?? string.Empty,
            Status = status ?? string.Empty,
            DocumentType = documentType ?? string.Empty,
            Page = page < 1 ? 1 : page
        };

        if (!string.IsNullOrWhiteSpace(model.Status) && !WorkflowStatus.IsKnown(model.Status))
        {
            return View(Copy(model, "Status filter is not a known workflow status."));
        }

        if (!string.IsNullOrWhiteSpace(model.DocumentType) && !DocumentTypes.IsKnown(DocumentTypes.Normalize(model.DocumentType)))
        {
            return View(Copy(model, "Document type filter must be PAN, AADHAR, or LOA."));
        }

        var result = await sql.ListDocumentsAsync(new DocumentListFilter
        {
            Page = model.Page,
            PageSize = model.PageSize,
            Status = string.IsNullOrWhiteSpace(model.Status) ? null : model.Status,
            DocumentType = string.IsNullOrWhiteSpace(model.DocumentType) ? null : DocumentTypes.Normalize(model.DocumentType),
            Search = string.IsNullOrWhiteSpace(model.Search) ? null : model.Search
        }, cancellationToken);

        return View(new DocumentsPage
        {
            Search = model.Search,
            Status = model.Status,
            DocumentType = model.DocumentType,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Items = result.Items
        });
    }

    // Role: Shows one document, its extracted text, and the approval or rejection message. Next: SqlTool.GetDocumentAsync in SqlTool.cs.
    [HttpGet("/documents/{documentId:guid}")]
    public async Task<IActionResult> Details(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await sql.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return View(new DetailsPage { Error = "This document could not be loaded." });
        }

        var ocr = await sql.GetLatestOcrResultAsync(documentId, cancellationToken);
        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        var compliance = history.LastOrDefault(entry => entry.Status is WorkflowStatus.Approved or WorkflowStatus.Rejected);
        return View(new DetailsPage
        {
            Document = document,
            Ocr = ocr,
            ComplianceDecision = compliance?.Status,
            ComplianceRemarks = compliance?.Message,
            CanRetry = WorkflowStatus.CanResume(document.CurrentStatus),
            Refresh = UiText.IsWorking(document.CurrentStatus),
            Notice = TempData["Notice"] as string
        });
    }

    // Role: Restarts a workflow that is not finished. Next: DocumentWorkflowService.RetryAsync in DocumentWorkflowRunner.cs.
    [HttpPost("/documents/{documentId:guid}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflows.RetryAsync(documentId, CorrelationId, cancellationToken);
            TempData["Notice"] = result.Message;
        }
        catch (Exception exception) when (exception is DocumentNotFoundException or InvalidWorkflowTransitionException or DuplicateProcessingException)
        {
            TempData["Notice"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { documentId });
    }

    // Role: Shows the audit trail, oldest first. Next: SqlTool.GetHistoryAsync in SqlTool.cs.
    [HttpGet("/documents/{documentId:guid}/history")]
    public async Task<IActionResult> History(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await sql.GetDocumentAsync(documentId, cancellationToken);
        if (document is null)
        {
            return View(new HistoryPage { DocumentId = documentId, Error = "Workflow history could not be loaded." });
        }

        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        return View(new HistoryPage { DocumentId = documentId, Items = history });
    }

    // Role: Shows the SQL compliance rules. Next: ComplianceRulesTool.GetRulesAsync in ComplianceRulesTool.cs.
    [HttpGet("/compliance-rules")]
    public async Task<IActionResult> Rules(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await rules.GetRulesAsync(null, cancellationToken);
            return View(new RulesPage { Rules = rows });
        }
        catch (Exception)
        {
            return View(new RulesPage { Error = "Compliance rules could not be loaded." });
        }
    }

    private Guid CorrelationId =>
        HttpContext.Items.TryGetValue(CorrelationMiddleware.ItemKey, out var value) && value is Guid id
            ? id
            : Guid.NewGuid();

    private static DocumentsPage Copy(DocumentsPage page, string error) => new()
    {
        Search = page.Search,
        Status = page.Status,
        DocumentType = page.DocumentType,
        Page = page.Page,
        PageSize = page.PageSize,
        Error = error
    };
}
