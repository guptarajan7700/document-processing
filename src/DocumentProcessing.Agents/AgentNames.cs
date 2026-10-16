namespace DocumentProcessing.Agents;

public static class AgentNames
{
    public const string Intake = "DocumentIntakeAgent";
    public const string VirusScan = "VirusScanAgent";
    public const string Ocr = "OcrAgent";
    public const string Compliance = "ComplianceAgent";
    public const string Coordinator = "CoordinatorAgent";
}

public static class ToolNames
{
    public const string SaveDocumentMetadata = "SqlTool.SaveDocumentMetadataAsync";
    public const string UpdateWorkflowStatus = "SqlTool.UpdateWorkflowStatusAsync";
    public const string SaveExtractedText = "SqlTool.SaveExtractedTextAsync";
    public const string ExtractText = "OcrTool.ExtractTextAsync";
    public const string GetRules = "ComplianceRulesTool.GetRulesAsync";
    public const string ValidateText = "ComplianceRulesTool.ValidateTextAsync";
    public const string VirusScan = "VirusScanTool.ScanAsync";
}

public interface IWorkflowHandoff
{
    void Handoff(Guid documentId);
}

public sealed class AgentStepResult
{
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
}
