using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Storage;
using DocumentProcessing.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<WorkflowOptions>(configuration.GetSection(WorkflowOptions.SectionName));
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<IDocumentFileStore, LocalDocumentFileStore>();
        return services;
    }
}
