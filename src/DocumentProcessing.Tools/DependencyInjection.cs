using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Tools.Compliance;
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.Tools.Sql;
using DocumentProcessing.Tools.VirusScan;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Tools;

public static class DependencyInjection
{
    public static IServiceCollection AddProcessingTools(this IServiceCollection services)
    {
        services.AddDbContext<DocumentQueryContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(SqlConnectionFactory.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{SqlConnectionFactory.ConnectionStringName}' is not configured. See README for LocalDB setup. Do not commit passwords.");
            }

            options.UseSqlServer(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
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
