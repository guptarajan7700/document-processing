using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Storage;

public sealed class LocalDocumentFileStore(IOptions<StorageOptions> options) : IDocumentFileStore
{
    private readonly StorageOptions _options = options.Value;

    public async Task<string> SaveAsync(Guid documentId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var safeName = Sanitize(fileName);
        var directory = GetDirectory(documentId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, safeName);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }

    public async Task<byte[]> ReadAsync(Guid documentId, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetDirectory(documentId), Sanitize(fileName));
        if (!File.Exists(path))
        {
            throw new PermanentToolException($"Stored file for document '{documentId}' was not found.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task DeleteAsync(Guid documentId, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetDirectory(documentId), Sanitize(fileName));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetDirectory(Guid documentId)
    {
        var root = _options.RootPath;
        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, root);
        }

        return Path.Combine(root, documentId.ToString("D"));
    }

    public static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw new DocumentValidationException("File name is invalid.");
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }
}
