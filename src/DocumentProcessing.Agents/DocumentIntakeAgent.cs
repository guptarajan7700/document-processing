using System.Diagnostics;
using DocumentProcessing.Agents.Logging;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Storage;
using DocumentProcessing.Infrastructure.Time;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Infrastructure.Telemetry;
using DocumentProcessing.Tools.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Agents;

public sealed class DocumentIntakeRequest
{
    public required string CustomerId { get; init; }
    public required string DocumentType { get; init; }
    public required string FileName { get; init; }
    public required string FileContentBase64 { get; init; }
    public Guid CorrelationId { get; init; }
}

public sealed class DocumentIntakeAgent(
    ISqlTool sql,
    IDocumentFileStore files,
    IWorkflowHandoff handoff,
    IClock clock,
    IOptions<WorkflowOptions> options,
    ILogger<DocumentIntakeAgent> logger)
{
    public async Task<Guid> AcceptAsync(DocumentIntakeRequest request, CancellationToken cancellationToken)
    {
        var correlationId = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId;
        using var activity = ProcessingTelemetry.Start("DocumentIntakeAgent", Guid.Empty, correlationId, AgentNames.Intake, ToolNames.SaveDocumentMetadata);
        using var scope = AgentLog.Begin(logger, correlationId, Guid.Empty, AgentNames.Intake, ToolNames.SaveDocumentMetadata, null);
        var started = Stopwatch.StartNew();

        var customerId = (request.CustomerId ?? string.Empty).Trim();
        var documentType = DocumentTypes.Normalize(request.DocumentType ?? string.Empty);
        var fileName = (request.FileName ?? string.Empty).Trim();
        Validate(customerId, documentType, fileName, request.FileContentBase64);

        var content = Decode(request.FileContentBase64);
        if (content.Length == 0)
        {
            throw new DocumentValidationException("File content is empty.");
        }

        if (content.Length > options.Value.MaxUploadBytes)
        {
            throw new DocumentValidationException(
                $"File exceeds the {options.Value.MaxUploadBytes} byte limit. The selected file is {content.Length} bytes.");
        }

        var documentId = Guid.NewGuid();
        string? storagePath = null;
        try
        {
            storagePath = await files.SaveAsync(documentId, fileName, content, cancellationToken);
            await sql.SaveDocumentMetadataAsync(new DocumentRecord
            {
                DocumentId = documentId,
                CustomerId = customerId,
                DocumentType = documentType,
                FileName = LocalDocumentFileStore.Sanitize(fileName),
                UploadedOn = clock.UtcNow,
                CurrentStatus = WorkflowStatus.Received,
                StoragePath = storagePath
            }, cancellationToken);
        }
        catch
        {
            if (storagePath is not null)
            {
                await files.DeleteAsync(documentId, fileName, CancellationToken.None);
            }

            throw;
        }

        logger.LogInformation(
            "Intake accepted {DocumentId} in {DurationMs} ms and handed off to the coordinator",
            documentId,
            started.ElapsedMilliseconds);
        handoff.Handoff(documentId);
        return documentId;
    }

    private static void Validate(string customerId, string documentType, string fileName, string? base64)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(customerId) || customerId.Length > 64)
        {
            errors.Add("CustomerId is required and must be 64 characters or fewer.");
        }

        if (!DocumentTypes.IsKnown(documentType))
        {
            errors.Add("DocumentType must be PAN, AADHAR, or LOA.");
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || fileName.Contains("..", StringComparison.Ordinal))
        {
            errors.Add("FileName is required and must be a file name, not a path.");
        }

        if (string.IsNullOrWhiteSpace(base64))
        {
            errors.Add("FileContentBase64 is required.");
        }

        if (errors.Count > 0)
        {
            throw new DocumentValidationException(string.Join(" ", errors)) { Errors = errors };
        }
    }

    public static byte[] Decode(string base64)
    {
        var payload = base64.Trim();
        var comma = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
        {
            payload = payload[(comma + 1)..];
        }

        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException exception)
        {
            throw new DocumentValidationException("FileContentBase64 is not valid Base64.", exception);
        }
    }
}
