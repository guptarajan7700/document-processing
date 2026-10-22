using DocumentProcessing.Agents;
using DocumentProcessing.Api.Models;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Api.Controllers;

public sealed class UploadController(
    DocumentIntakeAgent intake,
    IOptions<WorkflowOptions> options,
    ILogger<UploadController> logger) : Controller
{
    // Role: Shows the upload form. Next: Index POST in this file when the form is submitted.
    [HttpGet("/upload")]
    public IActionResult Index() => View(new UploadViewModel { MaxUploadBytes = options.Value.MaxUploadBytes });

    // Role: Saves the chosen file through the same intake agent as the API. Next: DocumentIntakeAgent.AcceptAsync in DocumentIntakeAgent.cs.
    [HttpPost("/upload")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(string customerId, string documentType, IFormFile? file, CancellationToken cancellationToken)
    {
        var model = new UploadViewModel
        {
            CustomerId = customerId?.Trim() ?? string.Empty,
            DocumentType = documentType ?? string.Empty,
            MaxUploadBytes = options.Value.MaxUploadBytes
        };

        if (file is null || file.Length == 0)
        {
            model.Error = "Choose a file.";
            return View(model);
        }

        if (file.Length > model.MaxUploadBytes)
        {
            model.Error = $"File exceeds the {model.MaxUploadBytes} byte limit.";
            return View(model);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var documentId = await intake.AcceptAsync(new DocumentIntakeRequest
            {
                CustomerId = model.CustomerId,
                DocumentType = model.DocumentType,
                FileName = Path.GetFileName(file.FileName),
                FileContentBase64 = Convert.ToBase64String(buffer.ToArray()),
                CorrelationId = ControllerCorrelation.Read(HttpContext)
            }, cancellationToken);

            TempData["Notice"] = "Upload accepted. Validation is running.";
            return RedirectToAction(nameof(DocumentsController.Details), "Documents", new { documentId });
        }
        catch (DocumentValidationException exception)
        {
            model.Error = exception.Message;
            return View(model);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Upload could not be saved");
            model.Error = "The upload could not be saved. Check that SQL Server LocalDB is running.";
            return View(model);
        }
    }
}
