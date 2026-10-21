namespace DocumentProcessing.Infrastructure.Storage;

public interface IDocumentFileStore
{
    Task<string> SaveAsync(Guid documentId, string fileName, byte[] content, CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(Guid documentId, string fileName, CancellationToken cancellationToken);

    Task DeleteAsync(Guid documentId, string fileName, CancellationToken cancellationToken);
}
