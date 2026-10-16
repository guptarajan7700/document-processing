using DocumentProcessing.Api.Contracts;
using DocumentProcessing.Api.Hosting;
using DocumentProcessing.Agents;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Orchestration;
using DocumentProcessing.Tools.Sql;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
    DocumentIntakeAgent intake,
    DocumentWorkflowService workflows,
    ISqlTool sql,
    ILogger<DocumentsController> logger) : ControllerBase
{
    [HttpPost("upload")]
    [ProducesResponseType(typeof(UploadDocumentResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UploadDocumentResponse>> Upload([FromBody] UploadDocumentRequest request, CancellationToken cancellationToken)
    {
        var documentId = await intake.AcceptAsync(new DocumentIntakeRequest
        {
            CustomerId = request.CustomerId,
            DocumentType = request.DocumentType,
            FileName = request.FileName,
            FileContentBase64 = request.FileContentBase64,
            CorrelationId = CorrelationId
        }, cancellationToken);

        logger.LogInformation("Upload accepted {DocumentId}", documentId);
        var response = new UploadDocumentResponse { DocumentId = documentId };
        return Accepted($"/api/documents/{documentId}/status", response);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DocumentSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentSummaryResponse>> Summary(CancellationToken cancellationToken)
    {
        var counts = await sql.GetStatusCountsAsync(cancellationToken);
        return Ok(new DocumentSummaryResponse
        {
            Total = counts.Total,
            Received = counts.Received,
            OcrInProgress = counts.OcrInProgress,
            OcrFailed = counts.OcrFailed,
            OcrSuccess = counts.OcrSuccess,
            ComplianceInProgress = counts.ComplianceInProgress,
            Approved = counts.Approved,
            Rejected = counts.Rejected
        });
    }

    [HttpGet("activity")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowHistoryResponse>>> Activity([FromQuery] int take = 8, CancellationToken cancellationToken = default)
    {
        var rows = await sql.GetRecentActivityAsync(take, cancellationToken);
        return Ok(rows.Select(MapHistory).ToArray());
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedDocumentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedDocumentsResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? documentType = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(status) && !WorkflowStatus.IsKnown(status))
        {
            throw new DocumentValidationException("Status filter is not a known workflow status.");
        }

        if (!string.IsNullOrWhiteSpace(documentType) && !DocumentTypes.IsKnown(DocumentTypes.Normalize(documentType)))
        {
            throw new DocumentValidationException("Document type filter must be PAN, AADHAR, or LOA.");
        }

        var result = await sql.ListDocumentsAsync(new DocumentListFilter
        {
            Page = page,
            PageSize = pageSize,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            DocumentType = string.IsNullOrWhiteSpace(documentType) ? null : DocumentTypes.Normalize(documentType),
            Search = search
        }, cancellationToken);

        return Ok(new PagedDocumentsResponse
        {
            Items = result.Items.Select(MapDocument).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [HttpGet("{documentId:guid}")]
    [ProducesResponseType(typeof(DocumentDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDetailsResponse>> Get(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await sql.GetDocumentAsync(documentId, cancellationToken) ?? throw new DocumentNotFoundException(documentId);
        var ocr = await sql.GetLatestOcrResultAsync(documentId, cancellationToken);
        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        var complianceEntry = history.LastOrDefault(entry => entry.Status is WorkflowStatus.Approved or WorkflowStatus.Rejected);

        return Ok(new DocumentDetailsResponse
        {
            DocumentId = document.DocumentId,
            CustomerId = document.CustomerId,
            DocumentType = document.DocumentType,
            FileName = document.FileName,
            UploadedOn = document.UploadedOn,
            CurrentStatus = document.CurrentStatus,
            CanRetry = WorkflowStatus.CanResume(document.CurrentStatus),
            Ocr = ocr is null ? null : new OcrDetailsResponse
            {
                Provider = ocr.Provider,
                IsMock = ocr.IsMock,
                ExtractedIdentifier = ocr.ExtractedIdentifier,
                ExtractedText = ocr.ExtractedText,
                AttemptNumber = ocr.AttemptNumber,
                CreatedOn = ocr.CreatedOn
            },
            Compliance = complianceEntry is null ? null : new ComplianceDetailsResponse
            {
                Decision = complianceEntry.Status,
                Remarks = complianceEntry.Message
            }
        });
    }

    [HttpGet("/api/documents/status/{documentId:guid}")]
    [HttpGet("{documentId:guid}/status")]
    [ProducesResponseType(typeof(DocumentStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentStatusResponse>> Status(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await sql.GetDocumentAsync(documentId, cancellationToken) ?? throw new DocumentNotFoundException(documentId);
        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        return Ok(new DocumentStatusResponse
        {
            DocumentId = document.DocumentId,
            CurrentStatus = document.CurrentStatus,
            UploadedOn = document.UploadedOn,
            LastStatusOn = history.LastOrDefault()?.Timestamp
        });
    }

    [HttpGet("{documentId:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WorkflowHistoryResponse>>> History(Guid documentId, CancellationToken cancellationToken)
    {
        _ = await sql.GetDocumentAsync(documentId, cancellationToken) ?? throw new DocumentNotFoundException(documentId);
        var history = await sql.GetHistoryAsync(documentId, cancellationToken);
        return Ok(history.Select(MapHistory).ToArray());
    }

    [HttpPost("{documentId:guid}/retry")]
    [ProducesResponseType(typeof(WorkflowActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkflowActionResponse>> Retry(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await workflows.RetryAsync(documentId, CorrelationId, cancellationToken);
        var response = new WorkflowActionResponse
        {
            DocumentId = result.DocumentId,
            Status = result.Status,
            Outcome = result.Outcome,
            Message = result.Message
        };
        return result.Outcome == "Duplicate" ? Conflict(response) : Ok(response);
    }

    private Guid CorrelationId =>
        HttpContext.Items.TryGetValue(CorrelationMiddleware.ItemKey, out var value) && value is Guid id
            ? id
            : Guid.NewGuid();

    private static DocumentResponse MapDocument(DocumentRecord document) => new()
    {
        DocumentId = document.DocumentId,
        CustomerId = document.CustomerId,
        DocumentType = document.DocumentType,
        FileName = document.FileName,
        UploadedOn = document.UploadedOn,
        CurrentStatus = document.CurrentStatus,
        LatestMessage = document.LatestMessage
    };

    private static WorkflowHistoryResponse MapHistory(WorkflowHistoryEntry entry) => new()
    {
        Id = entry.Id,
        DocumentId = entry.DocumentId,
        Status = entry.Status,
        Message = entry.Message,
        Timestamp = entry.Timestamp,
        AgentName = entry.AgentName,
        ToolName = entry.ToolName,
        AttemptNumber = entry.AttemptNumber,
        CorrelationId = entry.CorrelationId,
        DurationMs = entry.DurationMs,
        ExceptionType = entry.ExceptionType
    };
}
