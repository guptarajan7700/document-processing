using DocumentProcessing.Agents;
#if WINDOWS
using DocumentProcessing.ImageOcr;
#endif
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.Api.Hosting;
using DocumentProcessing.Infrastructure;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Telemetry;
using DocumentProcessing.Orchestration;
using DocumentProcessing.Tools;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

#if WINDOWS
SubmittedFileTextExtractor.UseImageReader(WindowsImageReader.ReadAsync, WindowsImageReader.ProviderName);
#endif

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Document Processing API",
        Version = "v1",
        Description = "Compliance document intake, mock OCR, and sample rule validation. OCR in the default configuration is simulated."
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy =>
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProcessingTools();
builder.Services.AddProcessingAgents();
builder.Services.AddOrchestration();
builder.Services.AddSingleton<WorkflowWorkQueue>();
builder.Services.AddSingleton<IWorkflowHandoff, ChannelWorkflowHandoff>();
builder.Services.AddHostedService<WorkflowRecoveryWorker>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DocumentProcessing.Api"))
    .WithTracing(tracing => tracing
        .AddSource(ProcessingTelemetry.SourceName)
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());
builder.Services.PostConfigure<StorageOptions>(options =>
{
    if (!Path.IsPathRooted(options.RootPath))
    {
        options.RootPath = Path.Combine(builder.Environment.ContentRootPath, options.RootPath);
    }
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Document Processing API v1"));
app.UseCors("AngularDev");
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    ocrProvider = "MockOcrProvider",
    ocrIsMock = true
}));

app.Run();

public partial class Program;
