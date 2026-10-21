using System.Data;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Tools.Sql;

public sealed class SqlTool(DocumentQueryContext db, ISqlConnectionFactory connections, ILogger<SqlTool> logger) : ISqlTool
{
    // Role: Inserts the document and the first RECEIVED audit in one transaction. Next: dbo.sp_SaveDocument, then dbo.sp_UpdateWorkflowStatus.
    public async Task SaveDocumentMetadataAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var insert = new SqlCommand("dbo.sp_SaveDocument", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        })
        {
            insert.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = document.DocumentId;
            insert.Parameters.Add("@CustomerId", SqlDbType.NVarChar, 64).Value = document.CustomerId;
            insert.Parameters.Add("@DocumentType", SqlDbType.NVarChar, 20).Value = document.DocumentType;
            insert.Parameters.Add("@FileName", SqlDbType.NVarChar, 255).Value = document.FileName;
            insert.Parameters.Add("@UploadedOn", SqlDbType.DateTime2).Value = document.UploadedOn;
            insert.Parameters.Add("@CurrentStatus", SqlDbType.NVarChar, 50).Value = document.CurrentStatus;
            insert.Parameters.Add("@StoragePath", SqlDbType.NVarChar, 500).Value = (object?)document.StoragePath ?? DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteStatusProcedureAsync(connection, transaction, new WorkflowStatusUpdate
        {
            DocumentId = document.DocumentId,
            Status = document.CurrentStatus,
            Message = "Document metadata saved and workflow status set to RECEIVED.",
            AgentName = "DocumentIntakeAgent",
            ToolName = "SqlTool.SaveDocumentMetadataAsync"
        }, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // Role: Changes CurrentStatus and appends an audit row. Next: ExecuteStatusProcedureAsync in this file, which calls dbo.sp_UpdateWorkflowStatus.
    public async Task UpdateWorkflowStatusAsync(WorkflowStatusUpdate update, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await ExecuteStatusProcedureAsync(connection, transaction: null, update, cancellationToken);
    }

    // Role: Stores the extracted text. Next: dbo.sp_SaveOCRText, then OcrAgent sets OCR_Success.
    public async Task SaveExtractedTextAsync(OcrResultRecord result, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.sp_SaveOCRText", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = result.DocumentId;
        command.Parameters.Add("@ExtractedText", SqlDbType.NVarChar, -1).Value = result.ExtractedText;
        command.Parameters.Add("@ExtractedIdentifier", SqlDbType.NVarChar, 128).Value = (object?)result.ExtractedIdentifier ?? DBNull.Value;
        command.Parameters.Add("@Provider", SqlDbType.NVarChar, 64).Value = result.Provider;
        command.Parameters.Add("@IsMock", SqlDbType.Bit).Value = result.IsMock;
        command.Parameters.Add("@AttemptNumber", SqlDbType.Int).Value = result.AttemptNumber;
        var returnValue = command.Parameters.Add("@RETURN", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(cancellationToken);
        if ((int)returnValue.Value == 1)
        {
            throw new DocumentNotFoundException(result.DocumentId);
        }
    }

    // Role: Reads one Documents row. Next: the caller continues with that status.
    public async Task<DocumentRecord?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var row = await db.Documents
            .Where(document => document.DocumentId == documentId)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : MapDocument(row);
    }

    // Role: Reads the newest OCR row. Next: ComplianceTextValidator.Validate in ComplianceTextValidator.cs.
    public async Task<OcrResultRecord?> GetLatestOcrResultAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return await db.OcrResults
            .Where(result => result.DocumentId == documentId)
            .OrderByDescending(result => result.CreatedOn)
            .ThenByDescending(result => result.Id)
            .Select(result => new OcrResultRecord
            {
                Id = result.Id,
                DocumentId = result.DocumentId,
                ExtractedText = result.ExtractedText,
                ExtractedIdentifier = result.ExtractedIdentifier,
                Provider = result.Provider,
                IsMock = result.IsMock,
                AttemptNumber = result.AttemptNumber,
                CreatedOn = result.CreatedOn
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Role: Reads the audit rows, oldest first. Next: the caller uses them for routing or the history page.
    public async Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return await db.WorkflowStatuses
            .Where(entry => entry.DocumentId == documentId)
            .OrderBy(entry => entry.Timestamp)
            .ThenBy(entry => entry.Id)
            .Select(entry => new WorkflowHistoryEntry
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
            })
            .ToListAsync(cancellationToken);
    }

    // Role: Returns a filtered page of documents plus the latest audit message. Next: DocumentsController.List returns the JSON.
    public async Task<PagedResult<DocumentRecord>> ListDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var query = ApplyListFilter(db.Documents, filter);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(document => document.UploadedOn)
            .ThenBy(document => document.DocumentId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(document => new DocumentRecord
            {
                DocumentId = document.DocumentId,
                CustomerId = document.CustomerId,
                DocumentType = document.DocumentType,
                FileName = document.FileName,
                UploadedOn = document.UploadedOn,
                CurrentStatus = document.CurrentStatus,
                StoragePath = document.StoragePath,
                LockToken = document.LockToken,
                LockedUntil = document.LockedUntil,
                LatestMessage = db.WorkflowStatuses
                    .Where(entry => entry.DocumentId == document.DocumentId)
                    .OrderByDescending(entry => entry.Timestamp)
                    .ThenByDescending(entry => entry.Id)
                    .Select(entry => entry.Message)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentRecord>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    // Role: Counts documents by status for the dashboard. Next: DocumentsController.Summary returns the JSON.
    public async Task<DocumentStatusCounts> GetStatusCountsAsync(CancellationToken cancellationToken)
    {
        var groups = await db.Documents
            .GroupBy(document => document.CurrentStatus)
            .Select(group => new { Status = group.Key, Total = group.Count() })
            .ToListAsync(cancellationToken);

        var counts = new DocumentStatusCounts();
        foreach (var group in groups)
        {
            counts.Total += group.Total;
            switch (group.Status)
            {
                case WorkflowStatus.Received: counts.Received = group.Total; break;
                case WorkflowStatus.OcrInProgress: counts.OcrInProgress = group.Total; break;
                case WorkflowStatus.OcrFailed: counts.OcrFailed = group.Total; break;
                case WorkflowStatus.OcrSuccess: counts.OcrSuccess = group.Total; break;
                case WorkflowStatus.ComplianceInProgress: counts.ComplianceInProgress = group.Total; break;
                case WorkflowStatus.Approved: counts.Approved = group.Total; break;
                case WorkflowStatus.Rejected: counts.Rejected = group.Total; break;
            }
        }

        return counts;
    }

    // Role: Returns the newest audit rows across all documents. Next: DocumentsController.Activity returns the JSON.
    public async Task<IReadOnlyList<WorkflowHistoryEntry>> GetRecentActivityAsync(int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 50);
        return await db.WorkflowStatuses
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.Id)
            .Take(take)
            .Select(entry => new WorkflowHistoryEntry
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
            })
            .ToListAsync(cancellationToken);
    }

    // Role: Reads compliance rules. Next: dbo.sp_GetComplianceRules.
    public async Task<IReadOnlyList<ComplianceRuleRecord>> GetComplianceRulesAsync(string? documentType, bool activeOnly, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand("dbo.sp_GetComplianceRules", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@DocumentType", SqlDbType.NVarChar, 20).Value = (object?)documentType ?? DBNull.Value;
        command.Parameters.Add("@ActiveOnly", SqlDbType.Bit).Value = activeOnly;

        var rules = new List<ComplianceRuleRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(new ComplianceRuleRecord
            {
                RuleId = reader.GetInt32(0),
                DocumentType = reader.GetString(1),
                RuleName = reader.GetString(2),
                RuleKind = reader.GetString(3),
                ExpectedLength = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsActive = reader.GetBoolean(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return rules;
    }

    // Role: Locks the document so only one worker processes it. This stays SQL because UPDLOCK cannot be expressed in LINQ. Next: DocumentWorkflowService.RunFrameworkWorkflowAsync in DocumentWorkflowRunner.cs.
    public async Task<WorkflowLockResult> TryAcquireLockAsync(Guid documentId, Guid lockToken, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string? status = null;
        Guid? existingToken = null;
        DateTime? lockedUntil = null;

        await using (var select = new SqlCommand(
            """
            SELECT CurrentStatus, LockToken, LockedUntil
            FROM dbo.Documents WITH (UPDLOCK, ROWLOCK)
            WHERE DocumentId = @DocumentId;
            """,
            connection,
            transaction))
        {
            select.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = documentId;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return new WorkflowLockResult { State = WorkflowLockState.NotFound };
            }

            status = reader.GetString(0);
            existingToken = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            lockedUntil = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
        }

        if (WorkflowStatus.IsTerminal(status))
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkflowLockResult { State = WorkflowLockState.AlreadyComplete, CurrentStatus = status };
        }

        var now = DateTime.UtcNow;
        var heldByOther = existingToken.HasValue && existingToken != lockToken && lockedUntil.HasValue && lockedUntil.Value > now;
        if (heldByOther)
        {
            await transaction.CommitAsync(cancellationToken);
            return new WorkflowLockResult { State = WorkflowLockState.Conflict, CurrentStatus = status };
        }

        await using (var update = new SqlCommand(
            """
            UPDATE dbo.Documents
            SET LockToken = @LockToken, LockedUntil = @LockedUntil
            WHERE DocumentId = @DocumentId;
            """,
            connection,
            transaction))
        {
            update.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = documentId;
            update.Parameters.Add("@LockToken", SqlDbType.UniqueIdentifier).Value = lockToken;
            update.Parameters.Add("@LockedUntil", SqlDbType.DateTime2).Value = now.Add(lease);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Acquired processing lock for document {DocumentId}", documentId);
        return new WorkflowLockResult { State = WorkflowLockState.Acquired, CurrentStatus = status };
    }

    // Role: Clears LockToken after the workflow finishes. Next: none. This is the end of processing.
    public async Task ReleaseLockAsync(Guid documentId, Guid lockToken, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            UPDATE dbo.Documents
            SET LockToken = NULL, LockedUntil = NULL
            WHERE DocumentId = @DocumentId AND LockToken = @LockToken;
            """,
            connection);
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = documentId;
        command.Parameters.Add("@LockToken", SqlDbType.UniqueIdentifier).Value = lockToken;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Role: Finds unfinished documents whose lock has expired. Next: WorkflowRecoveryWorker.ExecuteOneAsync in WorkflowRecoveryWorker.cs.
    public async Task<IReadOnlyList<DocumentRecord>> GetRecoverableDocumentsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await db.Documents
            .Where(document => document.CurrentStatus == WorkflowStatus.Received
                || document.CurrentStatus == WorkflowStatus.OcrInProgress
                || document.CurrentStatus == WorkflowStatus.OcrSuccess
                || document.CurrentStatus == WorkflowStatus.ComplianceInProgress)
            .Where(document => document.LockedUntil == null || document.LockedUntil < now)
            .OrderBy(document => document.UploadedOn)
            .ToListAsync(cancellationToken);
        return rows.Select(MapDocument).ToArray();
    }

    // Role: Calls dbo.sp_UpdateWorkflowStatus, which checks fn_WorkflowTransitionAllowed. Next: the SQL procedure commits the status and audit row.
    private async Task ExecuteStatusProcedureAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        WorkflowStatusUpdate update,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("dbo.sp_UpdateWorkflowStatus", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = update.DocumentId;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = update.Status;
        command.Parameters.Add("@Message", SqlDbType.NVarChar, 1000).Value = Truncate(update.Message, 1000);
        command.Parameters.Add("@AgentName", SqlDbType.NVarChar, 100).Value = (object?)update.AgentName ?? DBNull.Value;
        command.Parameters.Add("@ToolName", SqlDbType.NVarChar, 150).Value = (object?)update.ToolName ?? DBNull.Value;
        command.Parameters.Add("@AttemptNumber", SqlDbType.Int).Value = (object?)update.AttemptNumber ?? DBNull.Value;
        command.Parameters.Add("@CorrelationId", SqlDbType.UniqueIdentifier).Value = (object?)update.CorrelationId ?? DBNull.Value;
        command.Parameters.Add("@DurationMs", SqlDbType.Int).Value = (object?)update.DurationMs ?? DBNull.Value;
        command.Parameters.Add("@ExceptionType", SqlDbType.NVarChar, 200).Value = (object?)Truncate(update.ExceptionType, 200) ?? DBNull.Value;
        command.Parameters.Add("@IsExplicitRetry", SqlDbType.Bit).Value = update.IsExplicitRetry;
        var returnValue = command.Parameters.Add("@RETURN", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(cancellationToken);
        var code = (int)returnValue.Value;
        if (code == 1)
        {
            throw new DocumentNotFoundException(update.DocumentId);
        }

        if (code == 2)
        {
            throw new InvalidWorkflowTransitionException(
                $"Stored procedure rejected transition to '{update.Status}' for document '{update.DocumentId}'.");
        }

        logger.LogInformation(
            "Status transition recorded for {DocumentId} to {Status} by {AgentName} via {ToolName}. DurationMs={DurationMs} ExceptionType={ExceptionType}",
            update.DocumentId,
            update.Status,
            update.AgentName,
            update.ToolName,
            update.DurationMs,
            update.ExceptionType);
    }

    private static IQueryable<DocumentRow> ApplyListFilter(IQueryable<DocumentRow> query, DocumentListFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(document => document.CurrentStatus == filter.Status);
        }

        if (!string.IsNullOrWhiteSpace(filter.DocumentType))
        {
            query = query.Where(document => document.DocumentType == filter.DocumentType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = $"%{EscapeLike(filter.Search.Trim())}%";
            query = query.Where(document =>
                EF.Functions.Like(document.CustomerId, search)
                || EF.Functions.Like(document.FileName, search)
                || EF.Functions.Like(document.DocumentId.ToString(), search));
        }

        return query;
    }

    private static DocumentRecord MapDocument(DocumentRow row) => new()
    {
        DocumentId = row.DocumentId,
        CustomerId = row.CustomerId,
        DocumentType = row.DocumentType,
        FileName = row.FileName,
        UploadedOn = row.UploadedOn,
        CurrentStatus = row.CurrentStatus,
        StoragePath = row.StoragePath,
        LockToken = row.LockToken,
        LockedUntil = row.LockedUntil
    };

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
