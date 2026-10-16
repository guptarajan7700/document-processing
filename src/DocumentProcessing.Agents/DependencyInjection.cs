using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddProcessingAgents(this IServiceCollection services)
    {
        services.AddScoped<DocumentIntakeAgent>();
        services.AddScoped<VirusScanAgent>();
        services.AddScoped<OcrAgent>();
        services.AddScoped<ComplianceAgent>();
        return services;
    }
}
