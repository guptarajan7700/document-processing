namespace DocumentProcessing.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "App_Data/documents";
}

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";

    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Per-tool timeout. Production default is 30 seconds.</summary>
    public int ToolTimeoutMilliseconds { get; set; } = 30_000;

    public int OcrDelayMinMilliseconds { get; set; } = 1000;
    public int OcrDelayMaxMilliseconds { get; set; } = 3000;
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;
    public int LockLeaseSeconds { get; set; } = 180;
    public int RecoveryPollSeconds { get; set; } = 15;
    public bool EnableRecoveryHost { get; set; } = true;
}

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public string Provider { get; set; } = "Mock";
    public string DefaultScenario { get; set; } = "SuccessFirstAttempt";
    public int TimeoutSimulationMilliseconds { get; set; } = 60_000;
}
