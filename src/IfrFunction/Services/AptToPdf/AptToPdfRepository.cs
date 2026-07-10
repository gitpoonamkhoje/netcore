using Microsoft.Data.SqlClient;
using IfrFunction.Configuration;
using IfrFunction.Models;

namespace IfrFunction.Services.AptToPdf;

public interface IAptToPdfRepository
{
    Task EnsureTablesAsync(CancellationToken cancellationToken);

    Task<AptToPdfJobSchedule?> GetJobScheduleAsync(string jobName, CancellationToken cancellationToken);

    Task UpdateJobScheduleStatusAsync(string jobName, string status, DateTime? nextRunTimeUtc, CancellationToken cancellationToken);

    Task<long> InsertProcessLogAsync(AptToPdfProcessLogRecord record, CancellationToken cancellationToken);

    Task InsertErrorLogAsync(AptToPdfErrorLogRecord record, CancellationToken cancellationToken);

    Task<long> InsertFileMetadataAsync(AptToPdfFileMetadataRecord record, CancellationToken cancellationToken);
}

public sealed class AptToPdfProcessLogRecord
{
    public required string JobName { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public int Attempts { get; init; }
    public string? OutputBlobName { get; init; }
    public string? Message { get; init; }
}

public sealed class AptToPdfErrorLogRecord
{
    public required string JobName { get; init; }
    public required string FileName { get; init; }
    public int Attempts { get; init; }
    public required string ErrorMessage { get; init; }
    public string? Details { get; init; }
}

public sealed class AptToPdfFileMetadataRecord
{
    public required string SourceFileName { get; init; }
    public required string SourceBlobPath { get; init; }
    public required string OutputBlobName { get; init; }
    public required string ArchiveBlobName { get; init; }
    public required string FileType { get; init; }
    public long FileSizeBytes { get; init; }
    public required string Status { get; init; }
}

public sealed class AptToPdfRepository : IAptToPdfRepository
{
    private readonly IEnvironmentResources _resources;

    public AptToPdfRepository(IEnvironmentResources resources)
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

            IF OBJECT_ID('MTB_APT.JOB_MASTER', 'U') IS NULL
            BEGIN
                CREATE TABLE MTB_APT.JOB_MASTER (
                    JobName NVARCHAR(100) NOT NULL PRIMARY KEY,
                    Description NVARCHAR(500) NULL,
                    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_APT_JOB_MASTER_CreatedAtUtc DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('MTB_APT.JOB_SCHEDULE', 'U') IS NULL
            BEGIN
                CREATE TABLE MTB_APT.JOB_SCHEDULE (
                    JobName NVARCHAR(100) NOT NULL PRIMARY KEY,
                    Frequency NVARCHAR(50) NOT NULL,
                    RunTime TIME(0) NULL,
                    NextRunTimeUtc DATETIME2(3) NOT NULL,
                    Enabled BIT NOT NULL CONSTRAINT DF_MTB_APT_JOB_SCHEDULE_Enabled DEFAULT 1,
                    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_MTB_APT_JOB_SCHEDULE_Status DEFAULT 'Idle',
                    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_APT_JOB_SCHEDULE_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('MTB_APT.PROCESS_LOG', 'U') IS NULL
            BEGIN
                CREATE TABLE MTB_APT.PROCESS_LOG (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    JobName NVARCHAR(100) NOT NULL,
                    FileName NVARCHAR(260) NOT NULL,
                    Status NVARCHAR(20) NOT NULL,
                    Attempts INT NOT NULL,
                    OutputBlobName NVARCHAR(500) NULL,
                    Message NVARCHAR(2000) NULL,
                    ProcessedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_APT_PROCESS_LOG_ProcessedAtUtc DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('MTB_APT.ERROR_LOG', 'U') IS NULL
            BEGIN
                CREATE TABLE MTB_APT.ERROR_LOG (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    JobName NVARCHAR(100) NOT NULL,
                    FileName NVARCHAR(260) NOT NULL,
                    Attempts INT NOT NULL,
                    ErrorMessage NVARCHAR(2000) NOT NULL,
                    Details NVARCHAR(MAX) NULL,
                    LoggedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_APT_ERROR_LOG_LoggedAtUtc DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('MTB_APT.FILE_METADATA', 'U') IS NULL
            BEGIN
                CREATE TABLE MTB_APT.FILE_METADATA (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    SourceFileName NVARCHAR(260) NOT NULL,
                    SourceBlobPath NVARCHAR(500) NOT NULL,
                    OutputBlobName NVARCHAR(500) NOT NULL,
                    ArchiveBlobName NVARCHAR(500) NOT NULL,
                    FileType NVARCHAR(50) NOT NULL,
                    FileSizeBytes BIGINT NULL,
                    Status CHAR(1) NOT NULL,
                    ProcessedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_MTB_APT_FILE_METADATA_ProcessedAtUtc DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM MTB_APT.JOB_MASTER WHERE JobName = 'APT_TO_PDF')
                INSERT INTO MTB_APT.JOB_MASTER (JobName, Description)
                VALUES ('APT_TO_PDF', 'APT-to-PDF batch pipeline');

            IF NOT EXISTS (SELECT 1 FROM MTB_APT.JOB_SCHEDULE WHERE JobName = 'APT_TO_PDF')
                INSERT INTO MTB_APT.JOB_SCHEDULE (JobName, Frequency, RunTime, NextRunTimeUtc, Enabled, Status)
                VALUES ('APT_TO_PDF', 'Daily', '20:00:00', SYSUTCDATETIME(), 1, 'Idle');
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AptToPdfJobSchedule?> GetJobScheduleAsync(string jobName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT JobName, Frequency, RunTime, NextRunTimeUtc, Enabled, Status
            FROM MTB_APT.JOB_SCHEDULE
            WHERE JobName = @JobName;
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AptToPdfJobSchedule
        {
            JobName = reader.GetString(0),
            Frequency = reader.GetString(1),
            RunTime = reader.IsDBNull(2) ? null : reader.GetTimeSpan(2),
            NextRunTimeUtc = reader.GetDateTime(3),
            Enabled = reader.GetBoolean(4),
            Status = reader.GetString(5)
        };
    }

    public async Task UpdateJobScheduleStatusAsync(
        string jobName,
        string status,
        DateTime? nextRunTimeUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE MTB_APT.JOB_SCHEDULE
            SET Status = @Status,
                NextRunTimeUtc = COALESCE(@NextRunTimeUtc, NextRunTimeUtc),
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE JobName = @JobName;
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@NextRunTimeUtc", (object?)nextRunTimeUtc ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> InsertProcessLogAsync(AptToPdfProcessLogRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO MTB_APT.PROCESS_LOG (JobName, FileName, Status, Attempts, OutputBlobName, Message)
            OUTPUT INSERTED.Id
            VALUES (@JobName, @FileName, @Status, @Attempts, @OutputBlobName, @Message);
            """;

        return await ExecuteInsertAsync(sql, command =>
        {
            command.Parameters.AddWithValue("@JobName", record.JobName);
            command.Parameters.AddWithValue("@FileName", record.FileName);
            command.Parameters.AddWithValue("@Status", record.Status);
            command.Parameters.AddWithValue("@Attempts", record.Attempts);
            command.Parameters.AddWithValue("@OutputBlobName", (object?)record.OutputBlobName ?? DBNull.Value);
            command.Parameters.AddWithValue("@Message", (object?)record.Message ?? DBNull.Value);
        }, cancellationToken);
    }

    public async Task InsertErrorLogAsync(AptToPdfErrorLogRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO MTB_APT.ERROR_LOG (JobName, FileName, Attempts, ErrorMessage, Details)
            VALUES (@JobName, @FileName, @Attempts, @ErrorMessage, @Details);
            """;

        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", record.JobName);
        command.Parameters.AddWithValue("@FileName", record.FileName);
        command.Parameters.AddWithValue("@Attempts", record.Attempts);
        command.Parameters.AddWithValue("@ErrorMessage", record.ErrorMessage);
        command.Parameters.AddWithValue("@Details", (object?)record.Details ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> InsertFileMetadataAsync(AptToPdfFileMetadataRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO MTB_APT.FILE_METADATA (
                SourceFileName, SourceBlobPath, OutputBlobName, ArchiveBlobName, FileType, FileSizeBytes, Status)
            OUTPUT INSERTED.Id
            VALUES (@SourceFileName, @SourceBlobPath, @OutputBlobName, @ArchiveBlobName, @FileType, @FileSizeBytes, @Status);
            """;

        return await ExecuteInsertAsync(sql, command =>
        {
            command.Parameters.AddWithValue("@SourceFileName", record.SourceFileName);
            command.Parameters.AddWithValue("@SourceBlobPath", record.SourceBlobPath);
            command.Parameters.AddWithValue("@OutputBlobName", record.OutputBlobName);
            command.Parameters.AddWithValue("@ArchiveBlobName", record.ArchiveBlobName);
            command.Parameters.AddWithValue("@FileType", record.FileType);
            command.Parameters.AddWithValue("@FileSizeBytes", record.FileSizeBytes);
            command.Parameters.AddWithValue("@Status", record.Status);
        }, cancellationToken);
    }

    private async Task<long> ExecuteInsertAsync(
        string sql,
        Action<SqlCommand> configure,
        CancellationToken cancellationToken)
    {
        await using var connection = _resources.CreateIfrConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        configure(command);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(id);
    }
}
