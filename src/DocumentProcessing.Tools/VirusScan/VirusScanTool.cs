namespace DocumentProcessing.Tools.VirusScan;

public sealed class VirusScanResult
{
    public required bool IsClean { get; init; }
    public required string Message { get; init; }
}

public interface IVirusScanTool
{
    Task<VirusScanResult> ScanAsync(string fileName, byte[] content, CancellationToken cancellationToken);
}

/// <summary>
/// Mock filename check used by VirusScanAgent. It is not a real antivirus engine.
/// </summary>
public sealed class MockVirusScanTool : IVirusScanTool
{
    // Role: Rejects a file whose name contains VIRUS. Next: SqlTool.UpdateWorkflowStatusAsync in SqlTool.cs.
    public Task<VirusScanResult> ScanAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        if (fileName.Contains("VIRUS", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new VirusScanResult
            {
                IsClean = false,
                Message = "Optional mock virus scan rejected the file because the name contains VIRUS. This is not a real antivirus engine."
            });
        }

        return Task.FromResult(new VirusScanResult
        {
            IsClean = true,
            Message = "Optional mock virus scan found no configured test signature."
        });
    }
}
