using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Tools.Sql;

public sealed class DocumentQueryContext(DbContextOptions<DocumentQueryContext> options) : DbContext(options)
{
    public DbSet<DocumentRow> Documents => Set<DocumentRow>();
    public DbSet<WorkflowStatusRow> WorkflowStatuses => Set<WorkflowStatusRow>();
    public DbSet<OcrResultRow> OcrResults => Set<OcrResultRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRow>(entity =>
        {
            entity.ToTable("Documents", "dbo");
            entity.HasKey(row => row.DocumentId);
            entity.Property(row => row.CustomerId).HasMaxLength(64);
            entity.Property(row => row.DocumentType).HasMaxLength(20);
            entity.Property(row => row.FileName).HasMaxLength(255);
            entity.Property(row => row.CurrentStatus).HasMaxLength(50);
            entity.Property(row => row.StoragePath).HasMaxLength(500);
            entity.Property(row => row.UploadedOn).HasColumnType("datetime2");
            entity.Property(row => row.LockedUntil).HasColumnType("datetime2");
        });

        modelBuilder.Entity<WorkflowStatusRow>(entity =>
        {
            entity.ToTable("DocumentWorkflowStatus", "dbo");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();
            entity.Property(row => row.Status).HasMaxLength(50);
            entity.Property(row => row.Message).HasMaxLength(1000);
            entity.Property(row => row.Timestamp).HasColumnType("datetime2");
            entity.Property(row => row.AgentName).HasMaxLength(100);
            entity.Property(row => row.ToolName).HasMaxLength(150);
            entity.Property(row => row.ExceptionType).HasMaxLength(200);
        });

        modelBuilder.Entity<OcrResultRow>(entity =>
        {
            entity.ToTable("DocumentOCRResults", "dbo");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();
            entity.Property(row => row.ExtractedText).HasColumnType("nvarchar(max)");
            entity.Property(row => row.ExtractedIdentifier).HasMaxLength(128);
            entity.Property(row => row.Provider).HasMaxLength(64);
            entity.Property(row => row.CreatedOn).HasColumnType("datetime2");
        });
    }
}

public sealed class DocumentRow
{
    public Guid DocumentId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedOn { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public string? StoragePath { get; set; }
    public Guid? LockToken { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class WorkflowStatusRow
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? AgentName { get; set; }
    public string? ToolName { get; set; }
    public int? AttemptNumber { get; set; }
    public Guid? CorrelationId { get; set; }
    public int? DurationMs { get; set; }
    public string? ExceptionType { get; set; }
}

public sealed class OcrResultRow
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string? ExtractedIdentifier { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool IsMock { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime CreatedOn { get; set; }
}
