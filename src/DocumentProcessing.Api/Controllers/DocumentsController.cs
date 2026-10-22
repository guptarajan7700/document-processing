using DocumentProcessing.Agents;
using DocumentProcessing.Api.Models;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Orchestration;
using DocumentProcessing.Tools.Sql;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.Api.Controllers;

public sealed class DocumentsController(
    DocumentWorkflowService workflows,
    ISqlTool sql,
    ILogger<DocumentsController> logger) : Controller
{
    // Role: Shows the filtered document list. Next: SqlTool.ListDocumentsAsync in SqlTool.cs.
    [HttpGet("/documents")]
    public async Task<IActionResult> Index(string? search, string? status, string? documentType, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = new DocumentListViewModel
        {
            Search = search?.Trim() ?? string.Empty,
            Status = status ?? string.Empty,
            DocumentType = documentType ?? string.Empty,
            Page = page < 1 ? 1 : page
        };

        string? selectedStatus = null;
        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            if (!WorkflowStatus.TryCanonical(model.Status, out var canonicalStatus))
            {
                return View(Copy(model, "Status filter is not a known workflow status."));
            }

            selectedStatus = canonicalStatus;
        }

        string? selectedType = null;
        if (!string.IsNullOrWhiteSpace(model.DocumentType))
        {
            selectedType = DocumentTypes.Normalize(model.DocumentType);
            if (!DocumentTypes.IsKnown(selectedType))
            {
                return View(Copy(model, "Document type filter must be PAN, AADHAR, or LOA."));
            }
        }

        try
        {
            var result = await sql.ListDocumentsAsync(new DocumentListFilter
            {
                Page = model.Page,
                PageSize = model.PageSize,
                Status = selectedStatus,
                DocumentType = selectedType,
                Search = string.IsNullOrWhiteSpace(model.Search) ? null : model.Search
            }, cancellationToken);

            return View(new DocumentListViewModel
            {
                Search = model.Search,
                Status = selectedStatus ?? string.Empty,
                DocumentType = selectedType ?? string.Empty,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalCount = result.TotalCount,
                Items = result.Items
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Document list could not be loaded");
            return View(Copy(model, "The document list could not be loaded. Check that SQL Server LocalDB is running."));
        }
    }

    // Role: Shows one document, its extracted text, and the approval or rejection message. Next: SqlTool.GetDocumentAsync in SqlTool.cs.
    [HttpGet("/documents/{documentId:guid}")]
    public async Task<IActionResult> Details(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var document = await sql.GetDocumentAsync(documentId, cancellationToken);
            if (document is null)
            {
                return View(new DocumentDetailsViewModel { Error = "This document could not be loaded." });
            }

            var ocr = await sql.GetLatestOcrResultAsync(documentId, cancellationToken);
            var history = await sql.GetHistoryAsync(documentId, cancellationToken);
            var compliance = history.LastOrDefault(entry => entry.Status is WorkflowStatus.Approved or WorkflowStatus.Rejected);
            var latest = history.LastOrDefault();
            var paused = document.CurrentStatus == WorkflowStatus.OcrSuccess
                && latest?.Message.StartsWith(ComplianceAgent.CompensationMessage, StringComparison.Ordinal) == true;
            var failure = document.CurrentStatus == WorkflowStatus.OcrFailed
                ? history.LastOrDefault(entry => entry.Status == WorkflowStatus.OcrFailed && entry.Message.StartsWith("OCR failed permanently:", StringComparison.Ordinal))?.Message
                    ?? history.LastOrDefault(entry => entry.Status == WorkflowStatus.OcrFailed)?.Message
                : compliance?.Message;

            return View(new DocumentDetailsViewModel
            {
                Document = document,
                Ocr = ocr,
                ComplianceDecision = compliance?.Status,
                ComplianceRemarks = failure,
                PausedMessage = paused ? latest!.Message : null,
                CanRetry = WorkflowStatus.CanResume(document.CurrentStatus),
                Refresh = StatusText.IsWorking(document.CurrentStatus) && !paused,
                Notice = TempData["Notice"] as string
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Document {DocumentId} could not be loaded", documentId);
            return View(new DocumentDetailsViewModel { Error = "This document could not be loaded. Check that SQL Server LocalDB is running." });
        }
    }

    // Role: Restarts a workflow that is not finished. Next: DocumentWorkflowService.RetryAsync in DocumentWorkflowRunner.cs.
    [HttpPost("/documents/{documentId:guid}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflows.RetryAsync(documentId, ControllerCorrelation.Read(HttpContext), cancellationToken);
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
        try
        {
            var document = await sql.GetDocumentAsync(documentId, cancellationToken);
            if (document is null)
            {
                return View(new WorkflowHistoryViewModel { DocumentId = documentId, Error = "Workflow history could not be loaded." });
            }

            var history = await sql.GetHistoryAsync(documentId, cancellationToken);
            return View(new WorkflowHistoryViewModel { DocumentId = documentId, Items = history });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "History for {DocumentId} could not be loaded", documentId);
            return View(new WorkflowHistoryViewModel { DocumentId = documentId, Error = "Workflow history could not be loaded. Check that SQL Server LocalDB is running." });
        }
    }

    private static DocumentListViewModel Copy(DocumentListViewModel page, string error) => new()
    {
        Search = page.Search,
        Status = page.Status,
        DocumentType = page.DocumentType,
        Page = page.Page,
        PageSize = page.PageSize,
        Error = error
    };
}
