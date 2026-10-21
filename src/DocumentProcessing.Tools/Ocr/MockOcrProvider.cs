using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Workflow;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Tools.Ocr;

/// <summary>
/// Reads text from the submitted file and can simulate timeouts and failures.
/// Text files are decoded directly. Images are read with Windows OCR when that engine is on this PC.
/// </summary>
public sealed class MockOcrProvider(
    IOcrScenarioSelector scenarios,
    IOptions<OcrOptions> ocrOptions,
    IOptions<WorkflowOptions> workflowOptions) : IOcrProvider
{
    private int _callCount;

    public string Name => "MockOcrProvider";
    public bool IsMock => true;
    public int CallCount => _callCount;

    // Role: Simulates delay or failure, then reads the submitted file text. Next: SubmittedFileTextExtractor.ReadAsync in SubmittedFileTextExtractor.cs.
    public async Task<OcrExtraction> ExtractTextAsync(OcrExtractionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        var scenario = scenarios.Resolve(request.DocumentId, request.FileName);

        if (scenario == OcrScenario.PermanentFailure)
        {
            throw new PermanentToolException("Mock OCR provider simulated a permanent extraction failure.");
        }

        if (scenario == OcrScenario.FailThenSucceed && request.AttemptNumber == 1)
        {
            throw new TransientToolException("Mock OCR provider simulated a transient failure on the first attempt.");
        }

        var delay = scenario == OcrScenario.TimeoutEveryAttempt
            ? Math.Max(1, ocrOptions.Value.TimeoutSimulationMilliseconds)
            : NextDelay(workflowOptions.Value);

        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        var read = await SubmittedFileTextExtractor.ReadAsync(request.FileName, request.Content, cancellationToken);
        return new OcrExtraction
        {
            Text = read.Text,
            Provider = read.ImageProvider ?? Name,
            IsMock = read.ImageProvider is null
        };
    }

    // Role: Builds the old sample text used by tests only. Next: none in the live upload path.
    public static string BuildSampleText(string documentType) => documentType switch
    {
        DocumentTypes.Pan => "DOCUMENT TYPE: PAN\nNAME: SAMPLE HOLDER\nPAN: ABCDE1234F\nNOTE: MOCK OCR TEXT",
        DocumentTypes.Aadhar => "DOCUMENT TYPE: AADHAR\nNAME: SAMPLE HOLDER\nAADHAR: 123456789012\nNOTE: MOCK OCR TEXT",
        DocumentTypes.Loa => "DOCUMENT TYPE: LOA\nREFERENCE: LOA-1001\nNOTE: MOCK OCR TEXT",
        _ => "NOTE: MOCK OCR TEXT"
    };

    // Role: Picks the simulated OCR wait between 1 and 3 seconds. Next: ExtractTextAsync continues after the delay.
    private static int NextDelay(WorkflowOptions options)
    {
        var min = Math.Max(0, options.OcrDelayMinMilliseconds);
        var max = Math.Max(min, options.OcrDelayMaxMilliseconds);
        return min == max ? min : Random.Shared.Next(min, max + 1);
    }
}
