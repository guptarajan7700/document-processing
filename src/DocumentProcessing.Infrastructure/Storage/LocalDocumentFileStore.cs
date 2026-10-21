using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Storage;

public sealed class LocalDocumentFileStore(IOptions<StorageOptions> options) : IDocumentFileStore
{
    private readonly StorageOptions _options = options.Value;

    // Role: Writes the uploaded bytes to App_Data. Next: SqlTool.SaveDocumentMetadataAsync in SqlTool.cs.
    public async Task<string> SaveAsync(Guid documentId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var safeName = Sanitize(fileName);
        var directory = GetDirectory(documentId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, safeName);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }

    // Role: Reads the stored file for virus scan or OCR. Next: MockVirusScanTool.ScanAsync or OcrTool.ExtractTextAsync.
    public async Task<byte[]> ReadAsync(Guid documentId, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetDirectory(documentId), Sanitize(fileName));
        if (!File.Exists(path))
        {
            throw new PermanentToolException($"Stored file for document '{documentId}' was not found.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    // Role: Deletes the file when intake fails after the file was saved. Next: none. The upload returns an error.
    public Task DeleteAsync(Guid documentId, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetDirectory(documentId), Sanitize(fileName));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    // Role: Builds the folder path for one document. Next: SaveAsync or ReadAsync in this file writes or reads the file.
    private string GetDirectory(Guid documentId)
    {
        var root = _options.RootPath;
        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, root);
        }

        return Path.Combine(root, documentId.ToString("D"));
    }

    // Role: Keeps only the file name so a path cannot escape the folder. Next: SaveAsync in this file.
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
