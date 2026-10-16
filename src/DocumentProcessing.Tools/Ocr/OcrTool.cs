using System.Diagnostics;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Tools.Ocr;

public sealed class OcrTool(IOcrProvider provider, IOptions<WorkflowOptions> options, ILogger<OcrTool> logger) : IOcrTool
{
    public async Task<OcrExtraction> ExtractTextAsync(OcrExtractionRequest request, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1, options.Value.ToolTimeoutMilliseconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var started = Stopwatch.StartNew();

        try
        {
            var extraction = await provider.ExtractTextAsync(request, timeoutCts.Token);
            logger.LogInformation(
                "Tool {ToolName} completed for {DocumentId} on attempt {Attempt} in {DurationMs} ms. Provider={Provider} IsMock={IsMock}",
                "OcrTool.ExtractTextAsync",
                request.DocumentId,
                request.AttemptNumber,
                started.ElapsedMilliseconds,
                extraction.Provider,
                extraction.IsMock);
            return extraction;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Tool {ToolName} timed out for {DocumentId} on attempt {Attempt} after {DurationMs} ms",
                "OcrTool.ExtractTextAsync",
                request.DocumentId,
                request.AttemptNumber,
                started.ElapsedMilliseconds);
            throw new OcrTimeoutException(
                $"OCR tool timed out after {timeout.TotalMilliseconds:0} ms on attempt {request.AttemptNumber}.",
                timeout);
        }
    }
}
