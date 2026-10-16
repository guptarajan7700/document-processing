using DocumentProcessing.Tools.Compliance;
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.Tools.Sql;
using DocumentProcessing.Tools.VirusScan;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Tools;

public static class DependencyInjection
{
    public static IServiceCollection AddProcessingTools(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IOcrScenarioSelector, OcrScenarioSelector>();
        services.AddSingleton<IOcrProvider, MockOcrProvider>();
        services.AddSingleton<IOcrTool, OcrTool>();
        services.AddScoped<ISqlTool, SqlTool>();
        services.AddScoped<IComplianceRulesTool, ComplianceRulesTool>();
        services.AddSingleton<IVirusScanTool, MockVirusScanTool>();
        return services;
    }
}
