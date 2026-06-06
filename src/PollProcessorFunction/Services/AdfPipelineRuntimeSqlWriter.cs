using Microsoft.Data.SqlClient;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class AdfPipelineRuntimeSqlWriter : IAdfPipelineRuntimeSqlWriter
{
    private readonly IEnvironmentResources _resources;

    public AdfPipelineRuntimeSqlWriter(IEnvironmentResources resources)
    {
        _resources = resources;
    }

    public async Task<AdfPipelineRuntimeSqlSaveResult> SaveAsync(
        IReadOnlyList<AdfPipelineRuntimeInfoRow> rows,
        CancellationToken cancellationToken = default)
    {
        var snapshotId = Guid.NewGuid();
        var capturedAtUtc = DateTime.UtcNow;
        var inserted = 0;

        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureTableAsync(conn, (SqlTransaction)transaction, cancellationToken);

            foreach (var row in rows)
            {
                await InsertRowAsync(
                    conn,
                    (SqlTransaction)transaction,
                    snapshotId,
                    capturedAtUtc,
                    row,
                    cancellationToken);

                inserted++;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new AdfPipelineRuntimeSqlSaveResult
        {
            SnapshotId = snapshotId,
            CapturedAtUtc = capturedAtUtc,
            TotalRowsInserted = inserted
        };
    }

    private static async Task EnsureTableAsync(
        SqlConnection conn,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.AdfPipelineRuntimeInfoResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdfPipelineRuntimeInfoResult
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdfPipelineRuntimeInfoResult PRIMARY KEY,
        SnapshotId UNIQUEIDENTIFIER NOT NULL,
        CapturedAtUtc DATETIME2(3) NOT NULL,
        FolderName NVARCHAR(512) NULL,
        PipelineName NVARCHAR(256) NOT NULL,
        ObjectName NVARCHAR(512) NULL,
        ObjectCategory NVARCHAR(50) NOT NULL,
        ObjectType NVARCHAR(128) NULL,
        Status NVARCHAR(50) NULL,
        Description NVARCHAR(MAX) NULL,
        LatestPipelineRunStatus NVARCHAR(50) NULL,
        LatestPipelineRunId NVARCHAR(128) NULL,
        LatestPipelineRunStartUtc DATETIME2(3) NULL,
        LatestPipelineRunEndUtc DATETIME2(3) NULL,
        ExactTimeTakenSeconds FLOAT NULL,
        ExactTimeTaken NVARCHAR(50) NULL
    );

    CREATE INDEX IX_AdfPipelineRuntimeInfoResult_Snapshot
        ON dbo.AdfPipelineRuntimeInfoResult (SnapshotId, PipelineName, ObjectCategory);
END", conn, transaction);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRowAsync(
        SqlConnection conn,
        SqlTransaction transaction,
        Guid snapshotId,
        DateTime capturedAtUtc,
        AdfPipelineRuntimeInfoRow row,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(@"
INSERT INTO dbo.AdfPipelineRuntimeInfoResult
(
    SnapshotId,
    CapturedAtUtc,
    FolderName,
    PipelineName,
    ObjectName,
    ObjectCategory,
    ObjectType,
    Status,
    Description,
    LatestPipelineRunStatus,
    LatestPipelineRunId,
    LatestPipelineRunStartUtc,
    LatestPipelineRunEndUtc,
    ExactTimeTakenSeconds,
    ExactTimeTaken
)
VALUES
(
    @SnapshotId,
    @CapturedAtUtc,
    @FolderName,
    @PipelineName,
    @ObjectName,
    @ObjectCategory,
    @ObjectType,
    @Status,
    @Description,
    @LatestPipelineRunStatus,
    @LatestPipelineRunId,
    @LatestPipelineRunStartUtc,
    @LatestPipelineRunEndUtc,
    @ExactTimeTakenSeconds,
    @ExactTimeTaken
);", conn, transaction);

        cmd.Parameters.AddWithValue("@SnapshotId", snapshotId);
        cmd.Parameters.AddWithValue("@CapturedAtUtc", capturedAtUtc);
        AddNullableString(cmd, "@FolderName", row.FolderName, 512);
        cmd.Parameters.AddWithValue("@PipelineName", row.PipelineName);
        AddNullableString(cmd, "@ObjectName", row.ObjectName, 512);
        cmd.Parameters.AddWithValue("@ObjectCategory", row.ObjectCategory);
        AddNullableString(cmd, "@ObjectType", row.ObjectType, 128);
        AddNullableString(cmd, "@Status", row.Status, 50);
        AddNullableString(cmd, "@Description", row.Description, -1);
        AddNullableString(cmd, "@LatestPipelineRunStatus", row.LatestPipelineRunStatus, 50);
        AddNullableString(cmd, "@LatestPipelineRunId", row.LatestPipelineRunId, 128);
        AddNullableDateTime(cmd, "@LatestPipelineRunStartUtc", row.LatestPipelineRunStartUtc);
        AddNullableDateTime(cmd, "@LatestPipelineRunEndUtc", row.LatestPipelineRunEndUtc);
        AddNullableDouble(cmd, "@ExactTimeTakenSeconds", row.ExactTimeTakenSeconds);
        AddNullableString(cmd, "@ExactTimeTaken", row.ExactTimeTaken, 50);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableString(SqlCommand cmd, string name, string? value, int size)
    {
        var parameter = cmd.Parameters.Add(name, System.Data.SqlDbType.NVarChar, size);
        parameter.Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value;
    }

    private static void AddNullableDateTime(SqlCommand cmd, string name, DateTime? value)
    {
        var parameter = cmd.Parameters.Add(name, System.Data.SqlDbType.DateTime2);
        parameter.Value = value is null
            ? DBNull.Value
            : value.Value;
    }

    private static void AddNullableDouble(SqlCommand cmd, string name, double? value)
    {
        var parameter = cmd.Parameters.Add(name, System.Data.SqlDbType.Float);
        parameter.Value = value is null
            ? DBNull.Value
            : value.Value;
    }
}
