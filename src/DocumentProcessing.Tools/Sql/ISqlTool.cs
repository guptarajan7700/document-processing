using DocumentProcessing.Infrastructure.Models;

namespace DocumentProcessing.Tools.Sql;

public interface ISqlTool
{
    Task SaveDocumentMetadataAsync(DocumentRecord document, CancellationToken cancellationToken);

    Task UpdateWorkflowStatusAsync(WorkflowStatusUpdate update, CancellationToken cancellationToken);

    Task SaveExtractedTextAsync(OcrResultRecord result, CancellationToken cancellationToken);

    Task<DocumentRecord?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    Task<OcrResultRecord?> GetLatestOcrResultAsync(Guid documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken);

    Task<PagedResult<DocumentRecord>> ListDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken);

    Task<DocumentStatusCounts> GetStatusCountsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowHistoryEntry>> GetRecentActivityAsync(int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ComplianceRuleRecord>> GetComplianceRulesAsync(string? documentType, bool activeOnly, CancellationToken cancellationToken);

    Task<WorkflowLockResult> TryAcquireLockAsync(Guid documentId, Guid lockToken, TimeSpan lease, CancellationToken cancellationToken);

    Task ReleaseLockAsync(Guid documentId, Guid lockToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentRecord>> GetRecoverableDocumentsAsync(CancellationToken cancellationToken);
}
