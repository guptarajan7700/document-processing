using DocumentProcessing.Agents;
#if WINDOWS
using DocumentProcessing.ImageOcr;
#endif
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.mvc.Hosting;
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
        Description = "Compliance document intake. Text files are decoded. On Windows, images are read with Windows OCR, then checked against the PAN, AADHAR, and LOA rules."
    });
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProcessingTools();
builder.Services.AddProcessingAgents();
builder.Services.AddOrchestration();
builder.Services.AddSingleton<WorkflowWorkQueue>();
builder.Services.AddSingleton<IWorkflowHandoff, ChannelWorkflowHandoff>();
builder.Services.AddHostedService<WorkflowRecoveryWorker>();
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DocumentProcessing.mvc"))
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
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Document Processing API v1"));
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    ocrProvider = "MockOcrProvider",
    ocrIsMock = true,
    imageOcr = SubmittedFileTextExtractor.ImageProvider ?? "unavailable"
}));

app.Run();

public partial class Program;
