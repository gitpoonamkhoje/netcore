using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class AdfPipelineInfoSqlWriter : IAdfPipelineInfoSqlWriter
{
    private readonly IEnvironmentResources _resources;

    public AdfPipelineInfoSqlWriter(IEnvironmentResources resources)
    {
        _resources = resources;
    }

    public async Task<AdfPipelineInfoSqlSaveResult> SaveAsync(
        IReadOnlyList<AdfPipelineDetails> pipelines,
        CancellationToken cancellationToken = default)
    {
        var snapshotId = Guid.NewGuid();
        var capturedAtUtc = DateTime.UtcNow;
        var counters = new SaveCounters();

        await using var conn = _resources.CreateSqlConnection();
        await conn.OpenAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureTableAsync(conn, (SqlTransaction)transaction, cancellationToken);

            foreach (var pipeline in pipelines)
            {
                await InsertRowAsync(
                    conn,
                    (SqlTransaction)transaction,
                    snapshotId,
                    capturedAtUtc,
                    "Pipeline",
                    pipeline.PipelineName,
                    pipeline.PipelineName,
                    "Pipeline",
                    null,
                    null,
                    null,
                    ToJson(pipeline.Pipeline),
                    cancellationToken);
                counters.PipelineCount++;

                foreach (var activity in pipeline.Activities)
                {
                    await InsertRowAsync(
                        conn,
                        (SqlTransaction)transaction,
                        snapshotId,
                        capturedAtUtc,
                        "Activity",
                        pipeline.PipelineName,
                        activity.Name,
                        activity.Type,
                        activity.Status,
                        activity.Description,
                        null,
                        JsonConvert.SerializeObject(activity, Formatting.Indented),
                        cancellationToken);
                    counters.ActivityCount++;
                }

                foreach (var activity in pipeline.ScriptActivities)
                {
                    await InsertRowAsync(
                        conn,
                        (SqlTransaction)transaction,
                        snapshotId,
                        capturedAtUtc,
                        "ScriptActivity",
                        pipeline.PipelineName,
                        activity.Name,
                        activity.Type,
                        activity.Status,
                        activity.Description,
                        null,
                        ToJson(activity.Scripts ?? activity.TypeProperties),
                        cancellationToken);
                    counters.ScriptActivityCount++;
                }

                foreach (var reference in pipeline.ReferencedResources)
                {
                    await InsertRowAsync(
                        conn,
                        (SqlTransaction)transaction,
                        snapshotId,
                        capturedAtUtc,
                        "Reference",
                        pipeline.PipelineName,
                        reference.ReferenceName,
                        reference.ReferenceType,
                        null,
                        null,
                        reference.Path,
                        JsonConvert.SerializeObject(reference, Formatting.Indented),
                        cancellationToken);
                    counters.ReferenceCount++;
                }

                foreach (var trigger in pipeline.Triggers)
                {
                    await InsertRowAsync(
                        conn,
                        (SqlTransaction)transaction,
                        snapshotId,
                        capturedAtUtc,
                        "Trigger",
                        pipeline.PipelineName,
                        trigger.Value<string>("name") ?? "",
                        trigger.SelectToken("properties.type")?.Value<string>() ?? "",
                        null,
                        null,
                        null,
                        ToJson(trigger),
                        cancellationToken);
                    counters.TriggerCount++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new AdfPipelineInfoSqlSaveResult
        {
            SnapshotId = snapshotId,
            CapturedAtUtc = capturedAtUtc,
            PipelineCount = counters.PipelineCount,
            ActivityCount = counters.ActivityCount,
            ScriptActivityCount = counters.ScriptActivityCount,
            ReferenceCount = counters.ReferenceCount,
            TriggerCount = counters.TriggerCount,
            TotalRowsInserted = counters.TotalRowsInserted
        };
    }

    private static async Task EnsureTableAsync(
        SqlConnection conn,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.AdfPipelineInfoResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdfPipelineInfoResult
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdfPipelineInfoResult PRIMARY KEY,
        SnapshotId UNIQUEIDENTIFIER NOT NULL,
        CapturedAtUtc DATETIME2(3) NOT NULL,
        FeatureName NVARCHAR(50) NOT NULL,
        PipelineName NVARCHAR(256) NOT NULL,
        ObjectName NVARCHAR(512) NULL,
        ObjectType NVARCHAR(128) NULL,
        ObjectStatus NVARCHAR(50) NULL,
        Description NVARCHAR(MAX) NULL,
        JsonPath NVARCHAR(1000) NULL,
        JsonValue NVARCHAR(MAX) NULL
    );

    CREATE INDEX IX_AdfPipelineInfoResult_Snapshot
        ON dbo.AdfPipelineInfoResult (SnapshotId, FeatureName, PipelineName);
END;

IF COL_LENGTH(N'dbo.AdfPipelineInfoResult', N'ObjectStatus') IS NULL
BEGIN
    ALTER TABLE dbo.AdfPipelineInfoResult ADD ObjectStatus NVARCHAR(50) NULL;
END;

IF COL_LENGTH(N'dbo.AdfPipelineInfoResult', N'Description') IS NULL
BEGIN
    ALTER TABLE dbo.AdfPipelineInfoResult ADD Description NVARCHAR(MAX) NULL;
END", conn, transaction);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRowAsync(
        SqlConnection conn,
        SqlTransaction transaction,
        Guid snapshotId,
        DateTime capturedAtUtc,
        string featureName,
        string pipelineName,
        string? objectName,
        string? objectType,
        string? objectStatus,
        string? description,
        string? jsonPath,
        string? jsonValue,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(@"
INSERT INTO dbo.AdfPipelineInfoResult
(
    SnapshotId,
    CapturedAtUtc,
    FeatureName,
    PipelineName,
    ObjectName,
    ObjectType,
    ObjectStatus,
    Description,
    JsonPath,
    JsonValue
)
VALUES
(
    @SnapshotId,
    @CapturedAtUtc,
    @FeatureName,
    @PipelineName,
    @ObjectName,
    @ObjectType,
    @ObjectStatus,
    @Description,
    @JsonPath,
    @JsonValue
);", conn, transaction);

        cmd.Parameters.AddWithValue("@SnapshotId", snapshotId);
        cmd.Parameters.AddWithValue("@CapturedAtUtc", capturedAtUtc);
        cmd.Parameters.AddWithValue("@FeatureName", featureName);
        cmd.Parameters.AddWithValue("@PipelineName", pipelineName);
        AddNullableString(cmd, "@ObjectName", objectName, 512);
        AddNullableString(cmd, "@ObjectType", objectType, 128);
        AddNullableString(cmd, "@ObjectStatus", objectStatus, 50);
        AddNullableString(cmd, "@Description", description, -1);
        AddNullableString(cmd, "@JsonPath", jsonPath, 1000);
        AddNullableString(cmd, "@JsonValue", jsonValue, -1);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableString(SqlCommand cmd, string name, string? value, int size)
    {
        var parameter = cmd.Parameters.Add(name, System.Data.SqlDbType.NVarChar, size);
        parameter.Value = string.IsNullOrEmpty(value)
            ? DBNull.Value
            : value;
    }

    private static string ToJson(JToken? token)
    {
        return token is null
            ? ""
            : token.ToString(Formatting.Indented);
    }

    private sealed class SaveCounters
    {
        public int PipelineCount { get; set; }

        public int ActivityCount { get; set; }

        public int ScriptActivityCount { get; set; }

        public int ReferenceCount { get; set; }

        public int TriggerCount { get; set; }

        public int TotalRowsInserted =>
            PipelineCount + ActivityCount + ScriptActivityCount + ReferenceCount + TriggerCount;
    }
}
