using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Storage;

namespace DocumentProcessing.Tests.Support;

public sealed class MemoryFileStore : IDocumentFileStore
{
    private readonly Dictionary<string, byte[]> _files = new();

    public Task<string> SaveAsync(Guid documentId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var key = Key(documentId, fileName);
        _files[key] = content;
        return Task.FromResult(key);
    }

    public Task<byte[]> ReadAsync(Guid documentId, string fileName, CancellationToken cancellationToken)
    {
        var key = Key(documentId, fileName);
        if (!_files.TryGetValue(key, out var content))
        {
            throw new PermanentToolException($"Stored file for document '{documentId}' was not found.");
        }

        return Task.FromResult(content);
    }

    public Task DeleteAsync(Guid documentId, string fileName, CancellationToken cancellationToken)
    {
        _files.Remove(Key(documentId, fileName));
        return Task.CompletedTask;
    }

    private static string Key(Guid documentId, string fileName) =>
        documentId.ToString("D") + "/" + LocalDocumentFileStore.Sanitize(fileName);
}
