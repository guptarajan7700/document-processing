namespace DocumentProcessing.Tools.Ocr;

public enum OcrScenario
{
    SuccessFirstAttempt,
    FailThenSucceed,
    TimeoutEveryAttempt,
    PermanentFailure,
    SuccessThenInterruptBeforeCompliance
}

public sealed class OcrExtractionRequest
{
    public required Guid DocumentId { get; init; }
    public required string DocumentType { get; init; }
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
    public required int AttemptNumber { get; init; }
}

public sealed class OcrExtraction
{
    public required string Text { get; init; }
    public required string Provider { get; init; }
    public required bool IsMock { get; init; }
}

public interface IOcrProvider
{
    string Name { get; }
    bool IsMock { get; }
    Task<OcrExtraction> ExtractTextAsync(OcrExtractionRequest request, CancellationToken cancellationToken);
}

public interface IOcrScenarioSelector
{
    OcrScenario Resolve(Guid documentId, string fileName);
    void SetScenario(Guid documentId, OcrScenario scenario);
}

public interface IOcrTool
{
    Task<OcrExtraction> ExtractTextAsync(OcrExtractionRequest request, CancellationToken cancellationToken);
}
