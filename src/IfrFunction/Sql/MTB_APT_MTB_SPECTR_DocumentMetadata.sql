-- IFR diagram (2) APT & SPECTR — file metadata schemas
-- Run on IFR database (or target DB after IFR retirement)

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'MTB_APT')
    EXEC('CREATE SCHEMA MTB_APT');

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'MTB_SPECTR')
    EXEC('CREATE SCHEMA MTB_SPECTR');

IF OBJECT_ID('MTB_APT.DocumentMetadata', 'U') IS NULL
BEGIN
    CREATE TABLE MTB_APT.DocumentMetadata (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Source NVARCHAR(20) NOT NULL,
        SourceFileName NVARCHAR(260) NOT NULL,
        SourceSharePath NVARCHAR(500) NOT NULL,
        PdfFileName NVARCHAR(260) NULL,
        PdfSharePath NVARCHAR(500) NULL,
        ArchiveSharePath NVARCHAR(500) NULL,
        FileSizeBytes BIGINT NULL,
        SourceModifiedUtc DATETIME2(3) NULL,
        ProcessedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_APT_DocumentMetadata_ProcessedAtUtc DEFAULT SYSUTCDATETIME(),
        Status CHAR(1) NOT NULL,
        ErrorMessage NVARCHAR(2000) NULL
    );
END;

IF OBJECT_ID('MTB_SPECTR.DocumentMetadata', 'U') IS NULL
BEGIN
    CREATE TABLE MTB_SPECTR.DocumentMetadata (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Source NVARCHAR(20) NOT NULL,
        SourceFileName NVARCHAR(260) NOT NULL,
        SourceSharePath NVARCHAR(500) NOT NULL,
        PdfFileName NVARCHAR(260) NULL,
        PdfSharePath NVARCHAR(500) NULL,
        ArchiveSharePath NVARCHAR(500) NULL,
        FileSizeBytes BIGINT NULL,
        SourceModifiedUtc DATETIME2(3) NULL,
        ProcessedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_SPECTR_DocumentMetadata_ProcessedAtUtc DEFAULT SYSUTCDATETIME(),
        Status CHAR(1) NOT NULL,
        ErrorMessage NVARCHAR(2000) NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MTB_APT_DocumentMetadata_SourceSharePath' AND object_id = OBJECT_ID('MTB_APT.DocumentMetadata'))
    CREATE NONCLUSTERED INDEX IX_MTB_APT_DocumentMetadata_SourceSharePath
        ON MTB_APT.DocumentMetadata (SourceSharePath, Status);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MTB_SPECTR_DocumentMetadata_SourceSharePath' AND object_id = OBJECT_ID('MTB_SPECTR.DocumentMetadata'))
    CREATE NONCLUSTERED INDEX IX_MTB_SPECTR_DocumentMetadata_SourceSharePath
        ON MTB_SPECTR.DocumentMetadata (SourceSharePath, Status);
