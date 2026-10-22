using DocumentProcessing.Agents;
using DocumentProcessing.Infrastructure.Configuration;
using DocumentProcessing.Infrastructure.Exceptions;
using DocumentProcessing.Infrastructure.Models;
using DocumentProcessing.Infrastructure.Workflow;
using DocumentProcessing.Orchestration;
using DocumentProcessing.Tests.Support;
using DocumentProcessing.Tools.Compliance;
using DocumentProcessing.Tools.Ocr;
using DocumentProcessing.Tools.Sql;
using DocumentProcessing.Tools.VirusScan;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Tests;

public class WorkflowTransitionPolicyTests
{
    [Theory]
    [InlineData(WorkflowStatus.Received, WorkflowStatus.OcrInProgress, false, true)]
    [InlineData(WorkflowStatus.Received, WorkflowStatus.Rejected, false, true)]
    [InlineData(WorkflowStatus.OcrInProgress, WorkflowStatus.OcrSuccess, false, true)]
    [InlineData(WorkflowStatus.OcrInProgress, WorkflowStatus.OcrFailed, false, true)]
    [InlineData(WorkflowStatus.OcrFailed, WorkflowStatus.OcrInProgress, false, false)]
    [InlineData(WorkflowStatus.OcrFailed, WorkflowStatus.OcrInProgress, true, true)]
    [InlineData(WorkflowStatus.OcrSuccess, WorkflowStatus.ComplianceInProgress, false, true)]
    [InlineData(WorkflowStatus.Received, WorkflowStatus.ComplianceInProgress, false, false)]
    [InlineData(WorkflowStatus.ComplianceInProgress, WorkflowStatus.Approved, false, true)]
    [InlineData(WorkflowStatus.ComplianceInProgress, WorkflowStatus.Rejected, false, true)]
    [InlineData(WorkflowStatus.ComplianceInProgress, WorkflowStatus.OcrSuccess, false, true)]
    [InlineData(WorkflowStatus.Approved, WorkflowStatus.OcrInProgress, true, false)]
    public void Allows_only_documented_transitions(string from, string to, bool explicitRetry, bool allowed)
    {
        Assert.Equal(allowed, WorkflowTransitionPolicy.IsAllowed(from, to, explicitRetry));
    }
}

public class DocumentIdentifierExtractorTests
{
    [Fact]
    public void Extracts_pan_aadhar_and_loa_tokens()
    {
        Assert.Equal("ABCDE1234F", DocumentIdentifierExtractor.Extract(DocumentTypes.Pan, MockOcrProvider.BuildSampleText(DocumentTypes.Pan)));
        Assert.Equal("123456789012", DocumentIdentifierExtractor.Extract(DocumentTypes.Aadhar, MockOcrProvider.BuildSampleText(DocumentTypes.Aadhar)));
        Assert.Equal("LOA-1001", DocumentIdentifierExtractor.Extract(DocumentTypes.Loa, MockOcrProvider.BuildSampleText(DocumentTypes.Loa)));
        Assert.Equal("ABCDE1234F", DocumentIdentifierExtractor.Extract(DocumentTypes.Pan, "PAN ABCDE 1234 F"));
        Assert.Equal("123456789012", DocumentIdentifierExtractor.Extract(DocumentTypes.Aadhar, "AADHAR 1234 5678 9012"));
        Assert.Equal("LOA-12", DocumentIdentifierExtractor.Extract(DocumentTypes.Loa, "REFERENCE LOA-12"));
        Assert.Null(DocumentIdentifierExtractor.Extract(DocumentTypes.Loa, "REFERENCE LOA-1"));
    }

    [Fact]
    public void Rejects_short_pan_text()
    {
        var result = ComplianceTextValidator.Validate(DocumentTypes.Pan, "PAN: ABC", InMemorySqlTool.SeedRules());
        Assert.False(result.IsApproved);
        Assert.Contains("failed", result.Remarks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ComplianceTextValidator.AuthenticityLimitation, result.Remarks);
    }
}

public class DocumentIntakeAgentTests
{
    [Fact]
    public async Task Accepts_valid_upload_and_hands_off_document_id()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.Intake.AcceptAsync(new DocumentIntakeRequest
        {
            CustomerId = "CUST-1",
            DocumentType = "pan",
            FileName = "pan-card.txt",
            FileContentBase64 = Convert.ToBase64String("sample"u8.ToArray()),
            CorrelationId = Guid.NewGuid()
        }, CancellationToken.None);

        var document = await fixture.Sql.GetDocumentAsync(id, CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal(WorkflowStatus.Received, document.CurrentStatus);
        Assert.Equal(DocumentTypes.Pan, document.DocumentType);
        Assert.Contains(id, fixture.Handoffs);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, entry => entry.Status == WorkflowStatus.Received);
    }

    [Fact]
    public async Task Rejects_unknown_type_and_accepts_a_file_for_later_virus_scan()
    {
        var fixture = ProcessingFixture.Create();
        await Assert.ThrowsAsync<DocumentValidationException>(() => fixture.Intake.AcceptAsync(new DocumentIntakeRequest
        {
            CustomerId = "CUST-1",
            DocumentType = "PASSPORT",
            FileName = "a.txt",
            FileContentBase64 = Convert.ToBase64String("sample"u8.ToArray())
        }, CancellationToken.None));

        var id = await fixture.Intake.AcceptAsync(new DocumentIntakeRequest
        {
            CustomerId = "CUST-1",
            DocumentType = DocumentTypes.Pan,
            FileName = "VIRUS.txt",
            FileContentBase64 = Convert.ToBase64String("sample"u8.ToArray())
        }, CancellationToken.None);
        var document = await fixture.Sql.GetDocumentAsync(id, CancellationToken.None);
        Assert.Equal(WorkflowStatus.Received, document!.CurrentStatus);
    }
}

public class DocumentWorkflowServiceTests
{
    [Fact]
    public async Task Success_path_approves_sample_pan()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Completed", result.Outcome);
        Assert.Equal(WorkflowStatus.Approved, result.Status);
        Assert.Equal(1, fixture.Provider.CallCount);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);
        Assert.Equal(WorkflowStatus.Received, history[0].Status);
        Assert.Contains(history, entry => entry.Status == WorkflowStatus.OcrSuccess && entry.AgentName == AgentNames.Ocr);
        Assert.Contains(history, entry => entry.Status == WorkflowStatus.Approved);
    }

    [Fact]
    public async Task Submitted_file_without_a_matching_identifier_is_rejected()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan, "pan.txt", "name only, no identifier");
        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.Contains("No PAN value was found", result.Message, StringComparison.Ordinal);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compliance_rule_failure_rejects_document()
    {
        var fixture = ProcessingFixture.Create(new StaticOcrProvider("PAN: TOO-SHORT"));
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_document_throws()
    {
        var fixture = ProcessingFixture.Create();
        await Assert.ThrowsAsync<DocumentNotFoundException>(() =>
            fixture.Workflow.ExecuteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Transient_ocr_failure_succeeds_on_retry()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Aadhar, "aadhar.txt");
        fixture.Scenarios.SetScenario(id, OcrScenario.FailThenSucceed);

        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Approved, result.Status);
        Assert.Equal(2, fixture.Provider.CallCount);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, entry => entry.ExceptionType == nameof(TransientToolException) && entry.AttemptNumber == 1);
    }

    [Fact]
    public async Task Timeout_on_every_attempt_fails_permanently()
    {
        var fixture = ProcessingFixture.Create(workflow: new WorkflowOptions
        {
            MaxRetryAttempts = 2,
            ToolTimeoutMilliseconds = 100,
            OcrDelayMinMilliseconds = 0,
            OcrDelayMaxMilliseconds = 0,
            LockLeaseSeconds = 30
        }, ocr: new OcrOptions { TimeoutSimulationMilliseconds = 5_000 });
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        fixture.Scenarios.SetScenario(id, OcrScenario.TimeoutEveryAttempt);

        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.OcrFailed, result.Status);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, entry => entry.ExceptionType == nameof(OcrTimeoutException));
        Assert.Contains(history, entry => entry.Message == OcrAgent.CompensationMessage);
        Assert.DoesNotContain(history, entry => entry.AgentName == AgentNames.Compliance);
    }

    [Fact]
    public async Task Permanent_ocr_failure_compensates_and_keeps_history()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Loa);
        fixture.Scenarios.SetScenario(id, OcrScenario.PermanentFailure);

        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);
        var document = await fixture.Sql.GetDocumentAsync(id, CancellationToken.None);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);

        Assert.Equal(WorkflowStatus.OcrFailed, result.Status);
        Assert.Equal(WorkflowStatus.OcrFailed, document!.CurrentStatus);
        Assert.Contains(history, entry => entry.Message == OcrAgent.CompensationMessage);
        Assert.DoesNotContain(history, entry => entry.AgentName == AgentNames.Compliance);
    }

    [Fact]
    public async Task Explicit_retry_after_permanent_failure_can_succeed()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        fixture.Scenarios.SetScenario(id, OcrScenario.PermanentFailure);
        await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);
        fixture.Scenarios.SetScenario(id, OcrScenario.SuccessFirstAttempt);

        var retried = await fixture.Workflow.RetryAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Approved, retried.Status);
    }

    [Fact]
    public async Task Interrupt_after_ocr_replays_from_compliance_without_second_extraction()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan, "pan__interrupt.txt");
        var first = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("Interrupted", first.Outcome);
        Assert.Equal(WorkflowStatus.OcrSuccess, first.Status);
        Assert.Equal(1, fixture.Provider.CallCount);

        var replay = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(WorkflowStatus.Approved, replay.Status);
        Assert.Equal(1, fixture.Provider.CallCount);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, entry => entry.Message.StartsWith(WorkflowCheckpoints.InterruptMessagePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unfinished_ocr_success_resumes_at_compliance()
    {
        var fixture = ProcessingFixture.Create();
        var id = Guid.NewGuid();
        await fixture.Files.SaveAsync(id, "aadhar.txt", "sample"u8.ToArray(), CancellationToken.None);
        await fixture.Sql.SaveDocumentMetadataAsync(new DocumentRecord
        {
            DocumentId = id,
            CustomerId = "CUST-9",
            DocumentType = DocumentTypes.Aadhar,
            FileName = "aadhar.txt",
            UploadedOn = DateTime.UtcNow,
            CurrentStatus = WorkflowStatus.OcrSuccess
        }, CancellationToken.None);
        await fixture.Sql.SaveExtractedTextAsync(new OcrResultRecord
        {
            DocumentId = id,
            ExtractedText = MockOcrProvider.BuildSampleText(DocumentTypes.Aadhar),
            ExtractedIdentifier = "123456789012",
            Provider = "MockOcrProvider",
            IsMock = true,
            AttemptNumber = 1
        }, CancellationToken.None);

        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Approved, result.Status);
        Assert.Equal(0, fixture.Provider.CallCount);
    }

    [Fact]
    public async Task Completed_document_is_not_processed_again()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);
        var calls = fixture.Provider.CallCount;

        var second = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("AlreadyComplete", second.Outcome);
        Assert.Equal(calls, fixture.Provider.CallCount);
    }

    [Fact]
    public async Task Concurrent_execution_is_rejected_while_lock_is_held()
    {
        var blocker = new BlockingOcrProvider();
        var fixture = ProcessingFixture.Create(blocker);
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);

        var first = fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);
        await blocker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);
        blocker.Release.TrySetResult();
        var completed = await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Duplicate", second.Outcome);
        Assert.Equal(WorkflowStatus.Approved, completed.Status);
    }

    [Fact]
    public async Task Coordinator_does_not_route_received_documents_to_compliance()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        var route = await fixture.Coordinator.DecideNextRouteAsync(id, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(CoordinatorAgent.RouteVirusScan, route);

        await Assert.ThrowsAsync<InvalidWorkflowTransitionException>(() =>
            fixture.Compliance.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Virus_scan_rejection_stops_before_ocr()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan, "VIRUS-sample.txt");
        var result = await fixture.Workflow.ExecuteAsync(id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(WorkflowStatus.Rejected, result.Status);
        Assert.Equal(0, fixture.Provider.CallCount);
        var history = await fixture.Sql.GetHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, entry => entry.AgentName == AgentNames.VirusScan);
        Assert.DoesNotContain(history, entry => entry.AgentName == AgentNames.Ocr);
    }

    [Fact]
    public async Task In_progress_ocr_with_saved_text_recovers_without_restarting_ocr()
    {
        var fixture = ProcessingFixture.Create();
        var id = await fixture.SeedReceivedAsync(DocumentTypes.Pan);
        await fixture.Sql.UpdateWorkflowStatusAsync(new WorkflowStatusUpdate
        {
            DocumentId = id,
            Status = WorkflowStatus.OcrInProgress,
            Message = "OCR was interrupted.",
            AgentName = AgentNames.Ocr
        }, CancellationToken.None);
        await fixture.Sql.SaveExtractedTextAsync(new OcrResultRecord
        {
            DocumentId = id,
            ExtractedText = MockOcrProvider.BuildSampleText(DocumentTypes.Pan),
            Provider = "MockOcrProvider",
            IsMock = true,
            AttemptNumber = 1
        }, CancellationToken.None);

        var route = await fixture.Coordinator.DecideNextRouteAsync(id, Guid.NewGuid(), CancellationToken.None);
        var document = await fixture.Sql.GetDocumentAsync(id, CancellationToken.None);

        Assert.Equal(CoordinatorAgent.RouteCompliance, route);
        Assert.Equal(WorkflowStatus.OcrSuccess, document!.CurrentStatus);
    }
}

internal sealed class ProcessingFixture
{
    public required InMemorySqlTool Sql { get; init; }
    public required MemoryFileStore Files { get; init; }
    public required OcrScenarioSelector Scenarios { get; init; }
    public required MockOcrProvider Provider { get; init; }
    public required DocumentIntakeAgent Intake { get; init; }
    public required DocumentWorkflowService Workflow { get; init; }
    public required CoordinatorAgent Coordinator { get; init; }
    public required ComplianceAgent Compliance { get; init; }
    public required List<Guid> Handoffs { get; init; }

    public static ProcessingFixture Create(IOcrProvider? provider = null, WorkflowOptions? workflow = null, OcrOptions? ocr = null)
    {
        workflow ??= new WorkflowOptions
        {
            MaxRetryAttempts = 3,
            ToolTimeoutMilliseconds = 1000,
            OcrDelayMinMilliseconds = 0,
            OcrDelayMaxMilliseconds = 0,
            MaxUploadBytes = 1024 * 1024,
            LockLeaseSeconds = 30
        };
        ocr ??= new OcrOptions { DefaultScenario = nameof(OcrScenario.SuccessFirstAttempt), TimeoutSimulationMilliseconds = 5_000 };
        var clock = new MutableClock();
        var sql = new InMemorySqlTool(clock);
        var files = new MemoryFileStore();
        var scenarios = new OcrScenarioSelector(Options.Create(ocr));
        var mock = new MockOcrProvider(scenarios, Options.Create(ocr), Options.Create(workflow));
        var ocrTool = new OcrTool(provider ?? mock, Options.Create(workflow), NullLogger<OcrTool>.Instance);
        var rules = new ComplianceRulesTool(sql, new MemoryCache(new MemoryCacheOptions()));
        var handoffs = new List<Guid>();
        var intake = new DocumentIntakeAgent(
            sql,
            files,
            new ListHandoff(handoffs),
            clock,
            Options.Create(workflow),
            NullLogger<DocumentIntakeAgent>.Instance);
        var virusScan = new VirusScanAgent(sql, files, new MockVirusScanTool(), NullLogger<VirusScanAgent>.Instance);
        var ocrAgent = new OcrAgent(sql, files, ocrTool, Options.Create(workflow), NullLogger<OcrAgent>.Instance);
        var compliance = new ComplianceAgent(sql, rules, Options.Create(workflow), NullLogger<ComplianceAgent>.Instance);
        var coordinator = new CoordinatorAgent(sql, NullLogger<CoordinatorAgent>.Instance);
        var gate = new ScenarioCheckpointGate(sql, scenarios, NullLogger<ScenarioCheckpointGate>.Instance);
        var service = new DocumentWorkflowService(
            sql,
            coordinator,
            virusScan,
            ocrAgent,
            compliance,
            gate,
            Options.Create(workflow),
            NullLogger<DocumentWorkflowService>.Instance);

        return new ProcessingFixture
        {
            Sql = sql,
            Files = files,
            Scenarios = scenarios,
            Provider = mock,
            Intake = intake,
            Workflow = service,
            Coordinator = coordinator,
            Compliance = compliance,
            Handoffs = handoffs
        };
    }

    public async Task<Guid> SeedReceivedAsync(string documentType, string fileName = "sample.txt", string? fileText = null)
    {
        var id = Guid.NewGuid();
        var text = fileText ?? MockOcrProvider.BuildSampleText(documentType);
        await Files.SaveAsync(id, fileName, System.Text.Encoding.UTF8.GetBytes(text), CancellationToken.None);
        await Sql.SaveDocumentMetadataAsync(new DocumentRecord
        {
            DocumentId = id,
            CustomerId = "CUST-9",
            DocumentType = documentType,
            FileName = fileName,
            UploadedOn = DateTime.UtcNow,
            CurrentStatus = WorkflowStatus.Received
        }, CancellationToken.None);
        return id;
    }

    private sealed class ListHandoff(List<Guid> ids) : IWorkflowHandoff
    {
        public void Handoff(Guid documentId) => ids.Add(documentId);
    }
}

internal sealed class StaticOcrProvider(string text) : IOcrProvider
{
    public string Name => "StaticTestProvider";
    public bool IsMock => true;

    public Task<OcrExtraction> ExtractTextAsync(OcrExtractionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new OcrExtraction { Text = text, Provider = Name, IsMock = true });
}

internal sealed class BlockingOcrProvider : IOcrProvider
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Name => "BlockingTestProvider";
    public bool IsMock => true;

    public async Task<OcrExtraction> ExtractTextAsync(OcrExtractionRequest request, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return new OcrExtraction
        {
            Text = MockOcrProvider.BuildSampleText(request.DocumentType),
            Provider = Name,
            IsMock = true
        };
    }
}
