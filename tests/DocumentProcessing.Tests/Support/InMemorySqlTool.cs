using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Time;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Tools.Sql;

namespace DocumentProcessing.Tests.Support;

public sealed class InMemorySqlTool(IClock clock) : ISqlTool
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DocumentRecord> _documents = new();
    private readonly List<WorkflowHistoryEntry> _history = new();
    private readonly List<OcrResultRecord> _ocr = new();
    private readonly List<ComplianceRuleRecord> _rules = SeedRules();
    private long _historyId;
    private long _ocrId;

    public static List<ComplianceRuleRecord> SeedRules() =>
    [
        new()
        {
            RuleId = 1,
            DocumentType = DocumentTypes.Pan,
            RuleName = "PAN exact length",
            RuleKind = "ExactLength",
            ExpectedLength = 10,
            IsActive = true,
            Description = "Sample length rule."
        },
        new()
        {
            RuleId = 2,
            DocumentType = DocumentTypes.Aadhar,
            RuleName = "AADHAR exact length",
            RuleKind = "ExactLength",
            ExpectedLength = 12,
            IsActive = true,
            Description = "Sample length rule."
        },
        new()
        {
            RuleId = 3,
            DocumentType = DocumentTypes.Loa,
            RuleName = "LOA minimum reference length",
            RuleKind = "MinimumLength",
            ExpectedLength = 6,
            IsActive = true,
            Description = "Sample length rule."
        }
    ];

    public Task SaveDocumentMetadataAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _documents[document.DocumentId] = Copy(document);
            AddHistory(new WorkflowStatusUpdate
            {
                DocumentId = document.DocumentId,
                Status = document.CurrentStatus,
                Message = "Document metadata saved and workflow status set to RECEIVED.",
                AgentName = "DocumentIntakeAgent",
                ToolName = "SqlTool.SaveDocumentMetadataAsync"
            });
        }

        return Task.CompletedTask;
    }

    public Task UpdateWorkflowStatusAsync(WorkflowStatusUpdate update, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(update.DocumentId, out var document))
            {
                throw new DocumentProcessing.Infrastructure.Exceptions.DocumentNotFoundException(update.DocumentId);
            }

            WorkflowTransitionPolicy.Ensure(document.CurrentStatus, update.Status, update.IsExplicitRetry);
            document.CurrentStatus = update.Status;
            AddHistory(update);
        }

        return Task.CompletedTask;
    }

    public Task SaveExtractedTextAsync(OcrResultRecord result, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_documents.ContainsKey(result.DocumentId))
            {
                throw new DocumentProcessing.Infrastructure.Exceptions.DocumentNotFoundException(result.DocumentId);
            }

            result.Id = ++_ocrId;
            result.CreatedOn = NextStamp();
            _ocr.Add(result);
        }

        return Task.CompletedTask;
    }

    public Task<DocumentRecord?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(documentId, out var document) ? Copy(document) : null);
        }
    }

    public Task<OcrResultRecord?> GetLatestOcrResultAsync(Guid documentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var latest = _ocr.Where(item => item.DocumentId == documentId)
                .OrderByDescending(item => item.CreatedOn)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }
    }

    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WorkflowHistoryEntry> rows = _history
                .Where(item => item.DocumentId == documentId)
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => item.Id)
                .ToArray();
            return Task.FromResult(rows);
        }
    }

    public Task<PagedResult<DocumentRecord>> ListDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var query = _documents.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(item => item.CurrentStatus == filter.Status);
            }

            if (!string.IsNullOrWhiteSpace(filter.DocumentType))
            {
                query = query.Where(item => item.DocumentType == filter.DocumentType);
            }

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                query = query.Where(item =>
                    item.CustomerId.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) ||
                    item.FileName.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) ||
                    item.DocumentId.ToString().Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
            }

            var all = query.OrderByDescending(item => item.UploadedOn).ThenBy(item => item.DocumentId).ToArray();
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);
            return Task.FromResult(new PagedResult<DocumentRecord>
            {
                Items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(CopyWithLatestMessage).ToArray(),
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Length
            });
        }
    }

    public Task<DocumentStatusCounts> GetStatusCountsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var counts = new DocumentStatusCounts { Total = _documents.Count };
            foreach (var document in _documents.Values)
            {
                switch (document.CurrentStatus)
                {
                    case WorkflowStatus.Received: counts.Received++; break;
                    case WorkflowStatus.OcrInProgress: counts.OcrInProgress++; break;
                    case WorkflowStatus.OcrFailed: counts.OcrFailed++; break;
                    case WorkflowStatus.OcrSuccess: counts.OcrSuccess++; break;
                    case WorkflowStatus.ComplianceInProgress: counts.ComplianceInProgress++; break;
                    case WorkflowStatus.Approved: counts.Approved++; break;
                    case WorkflowStatus.Rejected: counts.Rejected++; break;
                }
            }

            return Task.FromResult(counts);
        }
    }

    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetRecentActivityAsync(int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WorkflowHistoryEntry> rows = _history
                .OrderByDescending(item => item.Timestamp)
                .ThenByDescending(item => item.Id)
                .Take(Math.Clamp(take, 1, 50))
                .ToArray();
            return Task.FromResult(rows);
        }
    }

    public Task<IReadOnlyList<ComplianceRuleRecord>> GetComplianceRulesAsync(string? documentType, bool activeOnly, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<ComplianceRuleRecord> rows = _rules
                .Where(rule => documentType is null || rule.DocumentType == documentType)
                .Where(rule => !activeOnly || rule.IsActive)
                .ToArray();
            return Task.FromResult(rows);
        }
    }

    public Task<WorkflowLockResult> TryAcquireLockAsync(Guid documentId, Guid lockToken, TimeSpan lease, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(documentId, out var document))
            {
                return Task.FromResult(new WorkflowLockResult { State = WorkflowLockState.NotFound });
            }

            if (WorkflowStatus.IsTerminal(document.CurrentStatus))
            {
                return Task.FromResult(new WorkflowLockResult { State = WorkflowLockState.AlreadyComplete, CurrentStatus = document.CurrentStatus });
            }

            var now = clock.UtcNow;
            var held = document.LockToken.HasValue && document.LockToken != lockToken && document.LockedUntil.HasValue && document.LockedUntil > now;
            if (held)
            {
                return Task.FromResult(new WorkflowLockResult { State = WorkflowLockState.Conflict, CurrentStatus = document.CurrentStatus });
            }

            document.LockToken = lockToken;
            document.LockedUntil = now.Add(lease);
            return Task.FromResult(new WorkflowLockResult { State = WorkflowLockState.Acquired, CurrentStatus = document.CurrentStatus });
        }
    }

    public Task ReleaseLockAsync(Guid documentId, Guid lockToken, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(documentId, out var document) && document.LockToken == lockToken)
            {
                document.LockToken = null;
                document.LockedUntil = null;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentRecord>> GetRecoverableDocumentsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = clock.UtcNow;
            IReadOnlyList<DocumentRecord> rows = _documents.Values
                .Where(document => document.CurrentStatus is WorkflowStatus.Received or WorkflowStatus.OcrInProgress or WorkflowStatus.OcrSuccess or WorkflowStatus.ComplianceInProgress)
                .Where(document => document.LockedUntil is null || document.LockedUntil < now)
                .OrderBy(document => document.UploadedOn)
                .Select(Copy)
                .ToArray();
            return Task.FromResult(rows);
        }
    }

    private void AddHistory(WorkflowStatusUpdate update)
    {
        _history.Add(new WorkflowHistoryEntry
        {
            Id = ++_historyId,
            DocumentId = update.DocumentId,
            Status = update.Status,
            Message = update.Message,
            Timestamp = NextStamp(),
            AgentName = update.AgentName,
            ToolName = update.ToolName,
            AttemptNumber = update.AttemptNumber,
            CorrelationId = update.CorrelationId,
            DurationMs = update.DurationMs,
            ExceptionType = update.ExceptionType
        });
    }

    private DateTime NextStamp() => clock.UtcNow.AddTicks(_historyId);

    private DocumentRecord CopyWithLatestMessage(DocumentRecord document)
    {
        var copy = Copy(document);
        copy.LatestMessage = _history
            .Where(item => item.DocumentId == document.DocumentId)
            .OrderByDescending(item => item.Timestamp)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault()?.Message;
        return copy;
    }

    private static DocumentRecord Copy(DocumentRecord document) => new()
    {
        DocumentId = document.DocumentId,
        CustomerId = document.CustomerId,
        DocumentType = document.DocumentType,
        FileName = document.FileName,
        UploadedOn = document.UploadedOn,
        CurrentStatus = document.CurrentStatus,
        StoragePath = document.StoragePath,
        LockToken = document.LockToken,
        LockedUntil = document.LockedUntil
    };
}
