using Microsoft.Data.SqlClient;
using IfrFunction.Configuration;
using IfrFunction.Models;

namespace IfrFunction.Services;

public interface IAptSpectrMetadataRepository
{
    Task EnsureTablesAsync(CancellationToken cancellationToken);

    Task<long> InsertMetadataAsync(AptSpectrMetadataRecord record, CancellationToken cancellationToken);

    Task<bool> IsAlreadyProcessedAsync(string schemaName, string sourceSharePath, CancellationToken cancellationToken);
}

public sealed class AptSpectrMetadataRecord
{
    public required string SchemaName { get; init; }
    public required string Source { get; init; }
    public required string SourceFileName { get; init; }
    public required string SourceSharePath { get; init; }
    public string? PdfFileName { get; init; }
    public string? PdfSharePath { get; init; }
    public string? ArchiveSharePath { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime? SourceModifiedUtc { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AptSpectrMetadataRepository : IAptSpectrMetadataRepository
{
    private readonly IEnvironmentResources _resources;

    public AptSpectrMetadataRepository(IEnvironmentResources resources)
    {
        _resources = resources;
    }

    public async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        if (!_resources.EnsureMetadataTables)
        {
            return;
        }

        const string sql = """
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
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> InsertMetadataAsync(AptSpectrMetadataRecord record, CancellationToken cancellationToken)
    {
        var table = $"{record.SchemaName}.{AptSpectrSchemas.MetadataTable}";
        var sql = $"""
            INSERT INTO {table} (
                Source, SourceFileName, SourceSharePath, PdfFileName, PdfSharePath, ArchiveSharePath,
                FileSizeBytes, SourceModifiedUtc, Status, ErrorMessage)
            OUTPUT INSERTED.Id
            VALUES (
                @Source, @SourceFileName, @SourceSharePath, @PdfFileName, @PdfSharePath, @ArchiveSharePath,
                @FileSizeBytes, @SourceModifiedUtc, @Status, @ErrorMessage);
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Source", record.Source);
        command.Parameters.AddWithValue("@SourceFileName", record.SourceFileName);
        command.Parameters.AddWithValue("@SourceSharePath", record.SourceSharePath);
        command.Parameters.AddWithValue("@PdfFileName", (object?)record.PdfFileName ?? DBNull.Value);
        command.Parameters.AddWithValue("@PdfSharePath", (object?)record.PdfSharePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@ArchiveSharePath", (object?)record.ArchiveSharePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@FileSizeBytes", record.FileSizeBytes);
        command.Parameters.AddWithValue("@SourceModifiedUtc", (object?)record.SourceModifiedUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", record.Status);
        command.Parameters.AddWithValue("@ErrorMessage", (object?)record.ErrorMessage ?? DBNull.Value);

        var id = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(id);
    }

    public async Task<bool> IsAlreadyProcessedAsync(
        string schemaName,
        string sourceSharePath,
        CancellationToken cancellationToken)
    {
        var table = $"{schemaName}.{AptSpectrSchemas.MetadataTable}";
        var sql = $"""
            SELECT TOP 1 1
            FROM {table}
            WHERE SourceSharePath = @SourceSharePath AND Status = @Status;
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SourceSharePath", sourceSharePath);
        command.Parameters.AddWithValue("@Status", ProcessingStatus.Complete);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }
}
