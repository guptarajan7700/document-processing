using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DocumentProcessing.ImageOcr;

public static class WindowsImageReader
{
    public const string ProviderName = "Windows.Media.Ocr";

    // Role: Reads characters from an image with the Windows OCR engine. Next: SubmittedFileTextExtractor.ReadAsync in SubmittedFileTextExtractor.cs.
    public static async Task<string?> ReadAsync(byte[] content)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null || content.Length == 0)
            {
                return null;
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(content);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var text = result.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
