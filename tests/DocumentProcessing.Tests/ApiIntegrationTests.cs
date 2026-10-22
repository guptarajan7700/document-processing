using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentProcessing.mvc.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace DocumentProcessing.Tests;

public class ApiIntegrationTests : IClassFixture<DocumentApiFactory>
{
    private readonly DocumentApiFactory _factory;

    public ApiIntegrationTests(DocumentApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_is_processed_to_approved_and_rules_come_from_sql()
    {
        using var client = _factory.CreateClient();
        var upload = await client.PostAsJsonAsync("/api/documents/upload", new UploadDocumentRequest
        {
            CustomerId = "INT-1007",
            DocumentType = "PAN",
            FileName = "integration-pan.txt",
            FileContentBase64 = Convert.ToBase64String("PAN ABCDE1234F"u8.ToArray())
        });

        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var accepted = await upload.Content.ReadFromJsonAsync<UploadDocumentResponse>();
        Assert.NotNull(accepted);

        DocumentStatusResponse? status = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            status = await client.GetFromJsonAsync<DocumentStatusResponse>($"/api/documents/{accepted.DocumentId}/status");
            if (status?.CurrentStatus is "APPROVED" or "REJECTED" or "OCR_FAILED")
            {
                break;
            }

            await Task.Delay(250);
        }

        Assert.NotNull(status);
        Assert.Equal("APPROVED", status.CurrentStatus);
        var alias = await client.GetFromJsonAsync<DocumentStatusResponse>($"/api/documents/status/{accepted.DocumentId}");
        Assert.Equal("APPROVED", alias!.CurrentStatus);

        var rules = await client.GetFromJsonAsync<JsonElement>("/api/compliance-rules");
        Assert.True(rules.GetArrayLength() >= 3);

        var list = await client.GetAsync("/api/documents?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fileContentBase64", listBody, StringComparison.OrdinalIgnoreCase);

        await DeleteDocumentAsync(accepted.DocumentId);
    }

    [Fact]
    public async Task Unknown_document_returns_not_found()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task DeleteDocumentAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(DocumentApiFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM dbo.DocumentWorkflowStatus WHERE DocumentId = @DocumentId;
            DELETE FROM dbo.DocumentOCRResults WHERE DocumentId = @DocumentId;
            DELETE FROM dbo.Documents WHERE DocumentId = @DocumentId;
            """;
        command.Parameters.Add(new SqlParameter("@DocumentId", documentId));
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class DocumentApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=DocumentProcessing;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DocumentProcessing", ConnectionString);
        builder.UseSetting("Workflow:OcrDelayMinMilliseconds", "0");
        builder.UseSetting("Workflow:OcrDelayMaxMilliseconds", "0");
        builder.UseSetting("Workflow:ToolTimeoutMilliseconds", "5000");
        builder.UseSetting("Workflow:EnableRecoveryHost", "true");
        builder.UseSetting("Workflow:RecoveryPollSeconds", "5");
    }
}
