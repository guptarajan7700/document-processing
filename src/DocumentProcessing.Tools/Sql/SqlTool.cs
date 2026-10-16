using System.Data;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Tools.Sql;

public sealed class SqlTool(ISqlConnectionFactory connections, ILogger<SqlTool> logger) : ISqlTool
{
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

    public async Task UpdateWorkflowStatusAsync(WorkflowStatusUpdate update, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await ExecuteStatusProcedureAsync(connection, transaction: null, update, cancellationToken);
    }

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

    public async Task<DocumentRecord?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT DocumentId, CustomerId, DocumentType, FileName, UploadedOn, CurrentStatus, StoragePath, LockToken, LockedUntil
            FROM dbo.Documents
            WHERE DocumentId = @DocumentId;
            """,
            connection);
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = documentId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadDocument(reader);
    }

    public async Task<OcrResultRecord?> GetLatestOcrResultAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT TOP (1) Id, DocumentId, ExtractedText, ExtractedIdentifier, Provider, IsMock, AttemptNumber, CreatedOn
            FROM dbo.DocumentOCRResults
            WHERE DocumentId = @DocumentId
            ORDER BY CreatedOn DESC, Id DESC;
            """,
            connection);
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = documentId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OcrResultRecord
        {
            Id = reader.GetInt64(0),
            DocumentId = reader.GetGuid(1),
            ExtractedText = reader.GetString(2),
            ExtractedIdentifier = reader.IsDBNull(3) ? null : reader.GetString(3),
            Provider = reader.GetString(4),
            IsMock = reader.GetBoolean(5),
            AttemptNumber = reader.GetInt32(6),
            CreatedOn = reader.GetDateTime(7)
        };
    }

    public async Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT Id, DocumentId, Status, Message, Timestamp, AgentName, ToolName, AttemptNumber, CorrelationId, DurationMs, ExceptionType
            FROM dbo.DocumentWorkflowStatus
            WHERE DocumentId = @DocumentId
            ORDER BY Timestamp ASC, Id ASC;
            """,
            connection);
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = documentId;
        return await ReadHistoryAsync(command, cancellationToken);
    }

    public async Task<PagedResult<DocumentRecord>> ListDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : $"%{EscapeLike(filter.Search.Trim())}%";

        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var count = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.Documents
            WHERE (@Status IS NULL OR CurrentStatus = @Status)
              AND (@DocumentType IS NULL OR DocumentType = @DocumentType)
              AND (@Search IS NULL OR CustomerId LIKE @Search OR FileName LIKE @Search OR CONVERT(nvarchar(36), DocumentId) LIKE @Search);
            """,
            connection);
        AddListParameters(count, filter, search);
        var total = (int)(await count.ExecuteScalarAsync(cancellationToken) ?? 0);

        await using var command = new SqlCommand(
            """
            SELECT d.DocumentId, d.CustomerId, d.DocumentType, d.FileName, d.UploadedOn, d.CurrentStatus, d.StoragePath, d.LockToken, d.LockedUntil, h.Message
            FROM dbo.Documents AS d
            OUTER APPLY (
                SELECT TOP (1) Message
                FROM dbo.DocumentWorkflowStatus
                WHERE DocumentId = d.DocumentId
                ORDER BY Timestamp DESC, Id DESC
            ) AS h
            WHERE (@Status IS NULL OR d.CurrentStatus = @Status)
              AND (@DocumentType IS NULL OR d.DocumentType = @DocumentType)
              AND (@Search IS NULL OR d.CustomerId LIKE @Search OR d.FileName LIKE @Search OR CONVERT(nvarchar(36), d.DocumentId) LIKE @Search)
            ORDER BY d.UploadedOn DESC, d.DocumentId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,
            connection);
        AddListParameters(command, filter, search);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (page - 1) * pageSize;
        command.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        var items = new List<DocumentRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var document = ReadDocument(reader);
            if (reader.FieldCount > 9 && !reader.IsDBNull(9))
            {
                document.LatestMessage = reader.GetString(9);
            }

            items.Add(document);
        }

        return new PagedResult<DocumentRecord>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<DocumentStatusCounts> GetStatusCountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT CurrentStatus, COUNT(*) AS Total
            FROM dbo.Documents
            GROUP BY CurrentStatus;
            """,
            connection);

        var counts = new DocumentStatusCounts();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(0);
            var total = reader.GetInt32(1);
            counts.Total += total;
            switch (status)
            {
                case WorkflowStatus.Received: counts.Received = total; break;
                case WorkflowStatus.OcrInProgress: counts.OcrInProgress = total; break;
                case WorkflowStatus.OcrFailed: counts.OcrFailed = total; break;
                case WorkflowStatus.OcrSuccess: counts.OcrSuccess = total; break;
                case WorkflowStatus.ComplianceInProgress: counts.ComplianceInProgress = total; break;
                case WorkflowStatus.Approved: counts.Approved = total; break;
                case WorkflowStatus.Rejected: counts.Rejected = total; break;
            }
        }

        return counts;
    }

    public async Task<IReadOnlyList<WorkflowHistoryEntry>> GetRecentActivityAsync(int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 50);
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT TOP (@Take) Id, DocumentId, Status, Message, Timestamp, AgentName, ToolName, AttemptNumber, CorrelationId, DurationMs, ExceptionType
            FROM dbo.DocumentWorkflowStatus
            ORDER BY Timestamp DESC, Id DESC;
            """,
            connection);
        command.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        var rows = await ReadHistoryAsync(command, cancellationToken);
        return rows;
    }

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

    public async Task<IReadOnlyList<DocumentRecord>> GetRecoverableDocumentsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT DocumentId, CustomerId, DocumentType, FileName, UploadedOn, CurrentStatus, StoragePath, LockToken, LockedUntil
            FROM dbo.Documents
            WHERE CurrentStatus IN (N'RECEIVED', N'OCR_IN_PROGRESS', N'OCR_Success', N'COMPLIANCE_CHECK_IN_PROGRESS')
              AND (LockedUntil IS NULL OR LockedUntil < SYSUTCDATETIME())
            ORDER BY UploadedOn ASC;
            """,
            connection);

        var items = new List<DocumentRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadDocument(reader));
        }

        return items;
    }

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

    private static void AddListParameters(SqlCommand command, DocumentListFilter filter, string? search)
    {
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = (object?)filter.Status ?? DBNull.Value;
        command.Parameters.Add("@DocumentType", SqlDbType.NVarChar, 20).Value = (object?)filter.DocumentType ?? DBNull.Value;
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 300).Value = (object?)search ?? DBNull.Value;
    }

    private static async Task<IReadOnlyList<WorkflowHistoryEntry>> ReadHistoryAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<WorkflowHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new WorkflowHistoryEntry
            {
                Id = reader.GetInt64(0),
                DocumentId = reader.GetGuid(1),
                Status = reader.GetString(2),
                Message = reader.GetString(3),
                Timestamp = reader.GetDateTime(4),
                AgentName = reader.IsDBNull(5) ? null : reader.GetString(5),
                ToolName = reader.IsDBNull(6) ? null : reader.GetString(6),
                AttemptNumber = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                CorrelationId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
                DurationMs = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                ExceptionType = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }

        return rows;
    }

    private static DocumentRecord ReadDocument(SqlDataReader reader) => new()
    {
        DocumentId = reader.GetGuid(0),
        CustomerId = reader.GetString(1),
        DocumentType = reader.GetString(2),
        FileName = reader.GetString(3),
        UploadedOn = reader.GetDateTime(4),
        CurrentStatus = reader.GetString(5),
        StoragePath = reader.IsDBNull(6) ? null : reader.GetString(6),
        LockToken = reader.IsDBNull(7) ? null : reader.GetGuid(7),
        LockedUntil = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
    };

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
