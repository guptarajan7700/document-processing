using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Orchestration;

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestration(this IServiceCollection services)
    {
        services.AddScoped<CoordinatorAgent>();
        services.AddScoped<IWorkflowCheckpointGate, ScenarioCheckpointGate>();
        services.AddScoped<DocumentWorkflowService>();
        return services;
    }
}
