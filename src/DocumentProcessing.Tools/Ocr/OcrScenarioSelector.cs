using System.Collections.Concurrent;
using DocumentProcessing.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Tools.Ocr;

public sealed class OcrScenarioSelector(IOptions<OcrOptions> options) : IOcrScenarioSelector
{
    private readonly ConcurrentDictionary<Guid, OcrScenario> _overrides = new();
    private readonly OcrScenario _default = Parse(options.Value.DefaultScenario);

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

    public void SetScenario(Guid documentId, OcrScenario scenario) => _overrides[documentId] = scenario;

    public static OcrScenario Parse(string? value) =>
        Enum.TryParse<OcrScenario>(value, ignoreCase: true, out var scenario)
            ? scenario
            : OcrScenario.SuccessFirstAttempt;
}
