using System.Collections.Concurrent;
using DocumentProcessing.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Tools.Ocr;

public sealed class OcrScenarioSelector(IOptions<OcrOptions> options) : IOcrScenarioSelector
{
    private readonly ConcurrentDictionary<Guid, OcrScenario> _overrides = new();
    private readonly OcrScenario _default = Parse(options.Value.DefaultScenario);

    // Role: Reads __failonce, __timeout, __permanent, or __interrupt from the file name. Next: MockOcrProvider.ExtractTextAsync in MockOcrProvider.cs.
    public OcrScenario Resolve(Guid documentId, string fileName)
    {
        if (_overrides.TryGetValue(documentId, out var scenario))
        {
            return scenario;
        }

        var name = fileName.ToLowerInvariant();
        if (name.Contains("__failonce", StringComparison.Ordinal))
        {
            return OcrScenario.FailThenSucceed;
        }

        if (name.Contains("__timeout", StringComparison.Ordinal))
        {
            return OcrScenario.TimeoutEveryAttempt;
        }

        if (name.Contains("__permanent", StringComparison.Ordinal))
        {
            return OcrScenario.PermanentFailure;
        }

        if (name.Contains("__interrupt", StringComparison.Ordinal))
        {
            return OcrScenario.SuccessThenInterruptBeforeCompliance;
        }

        return _default;
    }

    // Role: Forces a test scenario for one document. Next: Resolve in this file returns that scenario.
    public void SetScenario(Guid documentId, OcrScenario scenario) => _overrides[documentId] = scenario;

    // Role: Reads the default scenario from configuration. Next: Resolve in this file uses it when the file name has no marker.
    public static OcrScenario Parse(string? value) =>
        Enum.TryParse<OcrScenario>(value, ignoreCase: true, out var scenario)
            ? scenario
            : OcrScenario.SuccessFirstAttempt;
}
