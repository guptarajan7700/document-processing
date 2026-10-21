/*
    Document Processing Orchestration System
    LocalDB / SQL Server setup script.
    Safe to re-run. Does not store passwords.
*/
SET NOCOUNT ON;
GO

IF DB_ID(N'DocumentProcessing') IS NULL
BEGIN
    CREATE DATABASE DocumentProcessing;
END
GO

USE DocumentProcessing;
GO

CREATE OR ALTER FUNCTION dbo.fn_WorkflowTransitionAllowed
(
    @FromStatus NVARCHAR(50),
    @ToStatus NVARCHAR(50),
    @IsExplicitRetry BIT
)
RETURNS BIT
AS
BEGIN
    IF @FromStatus = @ToStatus
        RETURN 1;

    IF @FromStatus = N'RECEIVED' AND @ToStatus = N'OCR_IN_PROGRESS'
        RETURN 1;

    IF @FromStatus = N'RECEIVED' AND @ToStatus = N'REJECTED'
        RETURN 1;

    IF @FromStatus = N'OCR_IN_PROGRESS' AND @ToStatus IN (N'OCR_Success', N'OCR_FAILED')
        RETURN 1;

    IF @FromStatus = N'OCR_FAILED' AND @ToStatus = N'OCR_IN_PROGRESS' AND @IsExplicitRetry = 1
        RETURN 1;

    IF @FromStatus = N'OCR_Success' AND @ToStatus = N'COMPLIANCE_CHECK_IN_PROGRESS'
        RETURN 1;

    IF @FromStatus = N'COMPLIANCE_CHECK_IN_PROGRESS' AND @ToStatus IN (N'APPROVED', N'REJECTED', N'OCR_Success')
        RETURN 1;

    RETURN 0;
END
GO

IF OBJECT_ID(N'dbo.Documents', N'U') IS NULL
BEGIN
CREATE TABLE dbo.Documents
(
    DocumentId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Documents PRIMARY KEY,
    CustomerId NVARCHAR(64) NOT NULL,
    DocumentType NVARCHAR(20) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    UploadedOn DATETIME2 NOT NULL CONSTRAINT DF_Documents_UploadedOn DEFAULT SYSUTCDATETIME(),
    CurrentStatus NVARCHAR(50) NOT NULL,
    StoragePath NVARCHAR(500) NULL,
    LockToken UNIQUEIDENTIFIER NULL,
    LockedUntil DATETIME2 NULL,
    CONSTRAINT CK_Documents_DocumentType CHECK (DocumentType IN (N'PAN', N'AADHAR', N'LOA')),
    CONSTRAINT CK_Documents_CurrentStatus CHECK (CurrentStatus IN (
        N'RECEIVED',
        N'OCR_IN_PROGRESS',
        N'OCR_FAILED',
        N'OCR_Success',
        N'COMPLIANCE_CHECK_IN_PROGRESS',
        N'APPROVED',
        N'REJECTED'))
);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_CurrentStatus' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_CurrentStatus ON dbo.Documents (CurrentStatus, UploadedOn DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_DocumentType' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_DocumentType ON dbo.Documents (DocumentType, UploadedOn DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Documents_CustomerId' AND object_id = OBJECT_ID(N'dbo.Documents'))
    CREATE INDEX IX_Documents_CustomerId ON dbo.Documents (CustomerId);
GO

IF OBJECT_ID(N'dbo.DocumentWorkflowStatus', N'U') IS NULL
BEGIN
CREATE TABLE dbo.DocumentWorkflowStatus
(
    Id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DocumentWorkflowStatus PRIMARY KEY,
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    Status NVARCHAR(50) NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    Timestamp DATETIME2 NOT NULL CONSTRAINT DF_DocumentWorkflowStatus_Timestamp DEFAULT SYSUTCDATETIME(),
    AgentName NVARCHAR(100) NULL,
    ToolName NVARCHAR(150) NULL,
    AttemptNumber INT NULL,
    CorrelationId UNIQUEIDENTIFIER NULL,
    DurationMs INT NULL,
    ExceptionType NVARCHAR(200) NULL,
    CONSTRAINT FK_DocumentWorkflowStatus_Documents FOREIGN KEY (DocumentId) REFERENCES dbo.Documents (DocumentId),
    CONSTRAINT CK_DocumentWorkflowStatus_Status CHECK (Status IN (
        N'RECEIVED',
        N'OCR_IN_PROGRESS',
        N'OCR_FAILED',
        N'OCR_Success',
        N'COMPLIANCE_CHECK_IN_PROGRESS',
        N'APPROVED',
        N'REJECTED'))
);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentWorkflowStatus_Document_Time' AND object_id = OBJECT_ID(N'dbo.DocumentWorkflowStatus'))
    CREATE INDEX IX_DocumentWorkflowStatus_Document_Time ON dbo.DocumentWorkflowStatus (DocumentId, Timestamp, Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentWorkflowStatus_Timestamp' AND object_id = OBJECT_ID(N'dbo.DocumentWorkflowStatus'))
    CREATE INDEX IX_DocumentWorkflowStatus_Timestamp ON dbo.DocumentWorkflowStatus (Timestamp DESC);
GO

IF OBJECT_ID(N'dbo.DocumentOCRResults', N'U') IS NULL
BEGIN
CREATE TABLE dbo.DocumentOCRResults
(
    Id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DocumentOCRResults PRIMARY KEY,
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    ExtractedText NVARCHAR(MAX) NOT NULL,
    ExtractedIdentifier NVARCHAR(128) NULL,
    Provider NVARCHAR(64) NOT NULL,
    IsMock BIT NOT NULL,
    AttemptNumber INT NOT NULL,
    CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_DocumentOCRResults_CreatedOn DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_DocumentOCRResults_Documents FOREIGN KEY (DocumentId) REFERENCES dbo.Documents (DocumentId)
);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentOCRResults_Document' AND object_id = OBJECT_ID(N'dbo.DocumentOCRResults'))
    CREATE INDEX IX_DocumentOCRResults_Document ON dbo.DocumentOCRResults (DocumentId, CreatedOn DESC);
GO

IF OBJECT_ID(N'dbo.ComplianceRules', N'U') IS NULL
BEGIN
CREATE TABLE dbo.ComplianceRules
(
    RuleId INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ComplianceRules PRIMARY KEY,
    DocumentType NVARCHAR(20) NOT NULL,
    RuleName NVARCHAR(100) NOT NULL,
    RuleKind NVARCHAR(40) NOT NULL,
    ExpectedLength INT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ComplianceRules_IsActive DEFAULT (1),
    Description NVARCHAR(500) NULL,
    CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_ComplianceRules_CreatedOn DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_ComplianceRules_Type_Name UNIQUE (DocumentType, RuleName),
    CONSTRAINT CK_ComplianceRules_DocumentType CHECK (DocumentType IN (N'PAN', N'AADHAR', N'LOA')),
    CONSTRAINT CK_ComplianceRules_RuleKind CHECK (RuleKind IN (N'ExactLength', N'MinimumLength'))
);
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_SaveDocument
    @DocumentId UNIQUEIDENTIFIER,
    @CustomerId NVARCHAR(64),
    @DocumentType NVARCHAR(20),
    @FileName NVARCHAR(255),
    @UploadedOn DATETIME2,
    @CurrentStatus NVARCHAR(50),
    @StoragePath NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Documents
    (
        DocumentId, CustomerId, DocumentType, FileName, UploadedOn, CurrentStatus, StoragePath
    )
    VALUES
    (
        @DocumentId, @CustomerId, @DocumentType, @FileName, @UploadedOn, @CurrentStatus, @StoragePath
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateWorkflowStatus
    @DocumentId UNIQUEIDENTIFIER,
    @Status NVARCHAR(50),
    @Message NVARCHAR(1000),
    @AgentName NVARCHAR(100) = NULL,
    @ToolName NVARCHAR(150) = NULL,
    @AttemptNumber INT = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @DurationMs INT = NULL,
    @ExceptionType NVARCHAR(200) = NULL,
    @IsExplicitRetry BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OwnTransaction BIT = 0;
    IF @@TRANCOUNT = 0
    BEGIN
        SET @OwnTransaction = 1;
        BEGIN TRANSACTION;
    END

    BEGIN TRY
        DECLARE @CurrentStatus NVARCHAR(50);

        SELECT @CurrentStatus = CurrentStatus
        FROM dbo.Documents WITH (UPDLOCK, ROWLOCK)
        WHERE DocumentId = @DocumentId;

        IF @CurrentStatus IS NULL
        BEGIN
            IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            RETURN 1;
        END

        IF dbo.fn_WorkflowTransitionAllowed(@CurrentStatus, @Status, @IsExplicitRetry) = 0
        BEGIN
            IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            RETURN 2;
        END

        IF @CurrentStatus <> @Status
        BEGIN
            UPDATE dbo.Documents
            SET CurrentStatus = @Status
            WHERE DocumentId = @DocumentId;
        END

        INSERT INTO dbo.DocumentWorkflowStatus
        (
            DocumentId, Status, Message, Timestamp, AgentName, ToolName,
            AttemptNumber, CorrelationId, DurationMs, ExceptionType
        )
        VALUES
        (
            @DocumentId, @Status, @Message, SYSUTCDATETIME(), @AgentName, @ToolName,
            @AttemptNumber, @CorrelationId, @DurationMs, @ExceptionType
        );

        IF @OwnTransaction = 1 COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @OwnTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_SaveOCRText
    @DocumentId UNIQUEIDENTIFIER,
    @ExtractedText NVARCHAR(MAX),
    @ExtractedIdentifier NVARCHAR(128) = NULL,
    @Provider NVARCHAR(64),
    @IsMock BIT,
    @AttemptNumber INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Documents WHERE DocumentId = @DocumentId)
    BEGIN
        RETURN 1;
    END

    INSERT INTO dbo.DocumentOCRResults
    (
        DocumentId, ExtractedText, ExtractedIdentifier, Provider, IsMock, AttemptNumber
    )
    VALUES
    (
        @DocumentId, @ExtractedText, @ExtractedIdentifier, @Provider, @IsMock, @AttemptNumber
    );

    RETURN 0;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetComplianceRules
    @DocumentType NVARCHAR(20) = NULL,
    @ActiveOnly BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        RuleId,
        DocumentType,
        RuleName,
        RuleKind,
        ExpectedLength,
        IsActive,
        Description
    FROM dbo.ComplianceRules
    WHERE (@DocumentType IS NULL OR DocumentType = @DocumentType)
      AND (@ActiveOnly = 0 OR IsActive = 1)
    ORDER BY DocumentType, RuleId;
END
GO

MERGE dbo.ComplianceRules AS target
USING (VALUES
    (N'PAN', N'PAN exact length', N'ExactLength', 10, 1, N'Sample rule: the extracted PAN token must be 10 characters. Passing this check does not establish that the document is authentic.'),
    (N'AADHAR', N'AADHAR exact length', N'ExactLength', 12, 1, N'Sample rule: the extracted AADHAR token must be 12 digits. Passing this check does not establish that the document is authentic.'),
    (N'LOA', N'LOA minimum reference length', N'MinimumLength', 6, 1, N'Sample rule: the extracted LOA reference must be at least 6 characters. This is not an authenticity check.')
) AS source (DocumentType, RuleName, RuleKind, ExpectedLength, IsActive, Description)
ON target.DocumentType = source.DocumentType AND target.RuleName = source.RuleName
WHEN NOT MATCHED THEN
    INSERT (DocumentType, RuleName, RuleKind, ExpectedLength, IsActive, Description)
    VALUES (source.DocumentType, source.RuleName, source.RuleKind, source.ExpectedLength, source.IsActive, source.Description)
WHEN MATCHED THEN
    UPDATE SET
        RuleKind = source.RuleKind,
        ExpectedLength = source.ExpectedLength,
        IsActive = source.IsActive,
        Description = source.Description;
GO
