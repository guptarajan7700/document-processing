using System.Text;

namespace DocumentProcessing.Tools.Ocr;

public readonly record struct SubmittedFileText(string Text, string? ImageProvider);

public static class SubmittedFileTextExtractor
{
    public const string ImageUnreadMessage =
        "The submitted file is an image. Text could not be read from the image, so the identifier could not be checked.";

    public const string BinaryUnreadMessage =
        "The submitted file could not be decoded as text, so the identifier could not be checked.";

    private static Func<byte[], Task<string?>>? _imageReader;
    private static string? _imageProvider;

    // Role: Connects Windows OCR for image uploads. Next: ReadAsync in this file uses it when the file is an image.
    public static string? ImageProvider => _imageProvider;

    public static void UseImageReader(Func<byte[], Task<string?>> reader, string providerName)
    {
        _imageReader = reader;
        _imageProvider = providerName;
    }

    // Role: Decodes a text file, or reads an image with Windows OCR when that engine is available. Next: DocumentIdentifierExtractor.Extract in DocumentIdentifierExtractor.cs.
    public static async Task<SubmittedFileText> ReadAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length == 0)
        {
            return new SubmittedFileText(string.Empty, null);
        }

        if (IsImage(fileName, content))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_imageReader is null)
            {
                return new SubmittedFileText(ImageUnreadMessage, null);
            }

            var text = await _imageReader(content);
            return string.IsNullOrWhiteSpace(text)
                ? new SubmittedFileText(ImageUnreadMessage, _imageProvider)
                : new SubmittedFileText(text.Trim(), _imageProvider);
        }

        if (!LooksLikeText(fileName, content))
        {
            return new SubmittedFileText(BinaryUnreadMessage, null);
        }

        return new SubmittedFileText(Decode(content).Trim(), null);
    }

    // Role: Detects an image by extension or file header. Next: Read returns the image-unread message.
    private static bool IsImage(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return content.Length >= 8 && (
            content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            || (content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            || (content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46)
            || (content[0] == 0x42 && content[1] == 0x4D));
    }

    // Role: Accepts a text extension or mostly printable bytes. Next: Decode in this file.
    private static bool LooksLikeText(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var sampleLength = Math.Min(content.Length, 512);
        var printable = 0;
        for (var index = 0; index < sampleLength; index++)
        {
            var value = content[index];
            if (value == 0)
            {
                return false;
            }

            if (value is 9 or 10 or 13 || (value >= 32 && value < 127))
            {
                printable++;
            }
        }

        return printable >= sampleLength * 0.85;
    }

    // Role: Decodes UTF-8 or UTF-16 text. Next: DocumentIdentifierExtractor.Extract in DocumentIdentifierExtractor.cs.
    private static string Decode(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        return Encoding.UTF8.GetString(content);
    }
}
