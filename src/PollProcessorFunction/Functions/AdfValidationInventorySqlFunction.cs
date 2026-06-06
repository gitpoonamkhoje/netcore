using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions;

public sealed class AdfValidationInventorySqlFunction
{
    private static readonly Regex SqlTableNameRegex = new(
        @"\b(?:FROM|JOIN|UPDATE|INTO|MERGE\s+INTO)\s+([A-Za-z_][A-Za-z0-9_\[\]\.]*)(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAdfPipelineMetadataService _metadataService;
    private readonly IConfiguration _configuration;

    public AdfValidationInventorySqlFunction(
        IAdfPipelineMetadataService metadataService,
        IConfiguration configuration)
    {
        _metadataService = metadataService;
        _configuration = configuration;
    }

    [Function("SaveAdfValidationInventoryToSql")]
    public async Task<HttpResponseData> SaveAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "adf/validation-inventory/sql")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);
        var rows = BuildRows(pipelines);

        await using var conn = new SqlConnection(GetRequiredSetting("SqlConnectionString"));
        await conn.OpenAsync(cancellationToken);
        await EnsureTableAsync(conn, cancellationToken);

        var inserted = 0;
        var updated = 0;
        foreach (var row in rows)
        {
            if (await UpsertRowAsync(conn, row, cancellationToken))
            {
                inserted++;
            }
            else
            {
                updated++;
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonConvert.SerializeObject(new
            {
                Status = "Saved",
                TableName = "dbo.WorkflowComparisonConfig",
                Inserted = inserted,
                Updated = updated,
                Total = rows.Count
            }, Formatting.Indented),
            cancellationToken);

        return response;
    }

    private static IReadOnlyList<InventoryRow> BuildRows(IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var rows = new List<InventoryRow>();
        foreach (var pipeline in pipelines)
        {
            foreach (var activity in pipeline.Activities)
            {
                var sqlText = GetActivitySqlText(activity);
                var storedProcedure = GetStoredProcedureName(activity);
                var sourceDatasets = GetReferenceNames(activity.Inputs);
                var sinkDatasets = GetReferenceNames(activity.Outputs);
                var tableCandidates = DetectTableCandidates(sqlText);

                rows.Add(new InventoryRow
                {
                    WorkflowName = pipeline.PipelineName,
                    FolderName = GetFolderName(pipeline),
                    PipelineName = pipeline.PipelineName,
                    ActivityName = activity.Name,
                    ActivityType = activity.Type,
                    ActivityStatus = activity.Status,
                    DetectedStoredProcedure = storedProcedure,
                    DetectedSqlText = sqlText,
                    DetectedSourceDataset = string.Join(", ", sourceDatasets),
                    DetectedSinkDataset = string.Join(", ", sinkDatasets),
                    DetectedTableCandidates = string.Join(", ", tableCandidates),
                    SuggestedValidationType = GetSuggestedValidationType(activity, storedProcedure, sqlText, sourceDatasets, sinkDatasets),
                    SuggestedValidationTable = GetSuggestedValidationTable(tableCandidates, sinkDatasets, sourceDatasets),
                    ReviewStatus = GetReviewStatus(storedProcedure, sqlText, tableCandidates, sourceDatasets, sinkDatasets),
                    Notes = GetInventoryNotes(activity, storedProcedure, sqlText, tableCandidates)
                });
            }
        }

        return rows;
    }

    private static async Task EnsureTableAsync(SqlConnection conn, CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID(N'dbo.WorkflowComparisonConfig', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkflowComparisonConfig
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkflowComparisonConfig PRIMARY KEY,
        WorkflowName NVARCHAR(200) NOT NULL,
        FolderName NVARCHAR(512) NULL,
        PipelineName NVARCHAR(256) NOT NULL,
        ActivityName NVARCHAR(512) NULL,
        ActivityType NVARCHAR(128) NULL,
        ActivityStatus NVARCHAR(50) NULL,
        DetectedStoredProcedure NVARCHAR(512) NULL,
        DetectedSqlText NVARCHAR(MAX) NULL,
        DetectedSourceDataset NVARCHAR(MAX) NULL,
        DetectedSinkDataset NVARCHAR(MAX) NULL,
        DetectedTableCandidates NVARCHAR(MAX) NULL,
        SuggestedValidationType NVARCHAR(200) NULL,
        SuggestedValidationTable NVARCHAR(512) NULL,
        TableName NVARCHAR(512) NULL,
        KeyColumnsJson NVARCHAR(MAX) NULL,
        CompareColumnsJson NVARCHAR(MAX) NULL,
        WhereClause NVARCHAR(MAX) NULL,
        ParametersJson NVARCHAR(MAX) NULL,
        ReviewStatus NVARCHAR(50) NOT NULL CONSTRAINT DF_WorkflowComparisonConfig_ReviewStatus DEFAULT N'Needs Review',
        IsActive BIT NOT NULL CONSTRAINT DF_WorkflowComparisonConfig_IsActive DEFAULT 0,
        Notes NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_WorkflowComparisonConfig_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_WorkflowComparisonConfig_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_WorkflowComparisonConfig_Activity
        ON dbo.WorkflowComparisonConfig (WorkflowName, PipelineName, ActivityName);
END", conn);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> UpsertRowAsync(
        SqlConnection conn,
        InventoryRow row,
        CancellationToken cancellationToken)
    {
        await using var exists = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.WorkflowComparisonConfig
WHERE WorkflowName = @WorkflowName
  AND PipelineName = @PipelineName
  AND ActivityName = @ActivityName;", conn);

        AddParameters(exists, row);
        var existsValue = Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken));

        if (existsValue == 0)
        {
            await using var insert = new SqlCommand(@"
INSERT INTO dbo.WorkflowComparisonConfig
(
    WorkflowName,
    FolderName,
    PipelineName,
    ActivityName,
    ActivityType,
    ActivityStatus,
    DetectedStoredProcedure,
    DetectedSqlText,
    DetectedSourceDataset,
    DetectedSinkDataset,
    DetectedTableCandidates,
    SuggestedValidationType,
    SuggestedValidationTable,
    TableName,
    ReviewStatus,
    Notes
)
VALUES
(
    @WorkflowName,
    @FolderName,
    @PipelineName,
    @ActivityName,
    @ActivityType,
    @ActivityStatus,
    @DetectedStoredProcedure,
    @DetectedSqlText,
    @DetectedSourceDataset,
    @DetectedSinkDataset,
    @DetectedTableCandidates,
    @SuggestedValidationType,
    @SuggestedValidationTable,
    @SuggestedValidationTable,
    @ReviewStatus,
    @Notes
);", conn);

            AddParameters(insert, row);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }

        await using var update = new SqlCommand(@"
UPDATE dbo.WorkflowComparisonConfig
SET FolderName = @FolderName,
    ActivityType = @ActivityType,
    ActivityStatus = @ActivityStatus,
    DetectedStoredProcedure = @DetectedStoredProcedure,
    DetectedSqlText = @DetectedSqlText,
    DetectedSourceDataset = @DetectedSourceDataset,
    DetectedSinkDataset = @DetectedSinkDataset,
    DetectedTableCandidates = @DetectedTableCandidates,
    SuggestedValidationType = @SuggestedValidationType,
    SuggestedValidationTable = @SuggestedValidationTable,
    Notes = @Notes,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE WorkflowName = @WorkflowName
  AND PipelineName = @PipelineName
  AND ActivityName = @ActivityName;", conn);

        AddParameters(update, row);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return false;
    }

    private static void AddParameters(SqlCommand cmd, InventoryRow row)
    {
        AddString(cmd, "@WorkflowName", row.WorkflowName, 200);
        AddString(cmd, "@FolderName", row.FolderName, 512);
        AddString(cmd, "@PipelineName", row.PipelineName, 256);
        AddString(cmd, "@ActivityName", row.ActivityName, 512);
        AddString(cmd, "@ActivityType", row.ActivityType, 128);
        AddString(cmd, "@ActivityStatus", row.ActivityStatus, 50);
        AddString(cmd, "@DetectedStoredProcedure", row.DetectedStoredProcedure, 512);
        AddString(cmd, "@DetectedSqlText", row.DetectedSqlText, -1);
        AddString(cmd, "@DetectedSourceDataset", row.DetectedSourceDataset, -1);
        AddString(cmd, "@DetectedSinkDataset", row.DetectedSinkDataset, -1);
        AddString(cmd, "@DetectedTableCandidates", row.DetectedTableCandidates, -1);
        AddString(cmd, "@SuggestedValidationType", row.SuggestedValidationType, 200);
        AddString(cmd, "@SuggestedValidationTable", row.SuggestedValidationTable, 512);
        AddString(cmd, "@ReviewStatus", row.ReviewStatus, 50);
        AddString(cmd, "@Notes", row.Notes, -1);
    }

    private static void AddString(SqlCommand cmd, string name, string value, int size)
    {
        var parameter = cmd.Parameters.Add(name, System.Data.SqlDbType.NVarChar, size);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string GetFolderName(AdfPipelineDetails pipeline) =>
        pipeline.Pipeline.SelectToken("properties.folder.name")?.Value<string>() ?? "";

    private static string GetStoredProcedureName(AdfActivitySummary activity) =>
        activity.TypeProperties?.SelectToken("storedProcedureName")?.Value<string>()
        ?? activity.TypeProperties?.SelectToken("procedureName")?.Value<string>()
        ?? "";

    private static string GetActivitySqlText(AdfActivitySummary activity)
    {
        var scriptTexts = activity.Scripts?
            .SelectTokens("$..text")
            .Values<string>()
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray()
            ?? Array.Empty<string>();

        if (scriptTexts.Length > 0)
        {
            return string.Join(Environment.NewLine + Environment.NewLine, scriptTexts);
        }

        return activity.TypeProperties?.SelectToken("script")?.Value<string>()
            ?? activity.TypeProperties?.SelectToken("sqlReaderQuery")?.Value<string>()
            ?? activity.TypeProperties?.SelectToken("query")?.Value<string>()
            ?? "";
    }

    private static IReadOnlyList<string> GetReferenceNames(JToken? token) =>
        token?
            .SelectTokens("$..referenceName")
            .Values<string>()
            .Select(value => value ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? Array.Empty<string>();

    private static IReadOnlyList<string> DetectTableCandidates(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return Array.Empty<string>();
        }

        return SqlTableNameRegex
            .Matches(sqlText)
            .Select(match => match.Groups[1].Value.Trim('[', ']'))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetSuggestedValidationType(
        AdfActivitySummary activity,
        string storedProcedure,
        string sqlText,
        IReadOnlyList<string> sourceDatasets,
        IReadOnlyList<string> sinkDatasets)
    {
        if (!string.IsNullOrWhiteSpace(storedProcedure))
        {
            return "Stored procedure output validation";
        }

        if (!string.IsNullOrWhiteSpace(sqlText))
        {
            return "SQL script result validation";
        }

        if (string.Equals(activity.Type, "Copy", StringComparison.OrdinalIgnoreCase)
            || sourceDatasets.Count > 0
            || sinkDatasets.Count > 0)
        {
            return "Source/sink dataset validation";
        }

        return "Manual review";
    }

    private static string GetSuggestedValidationTable(
        IReadOnlyList<string> tableCandidates,
        IReadOnlyList<string> sinkDatasets,
        IReadOnlyList<string> sourceDatasets)
    {
        if (tableCandidates.Count > 0)
        {
            return tableCandidates[0];
        }

        if (sinkDatasets.Count > 0)
        {
            return sinkDatasets[0];
        }

        return sourceDatasets.Count > 0 ? sourceDatasets[0] : "";
    }

    private static string GetReviewStatus(
        string storedProcedure,
        string sqlText,
        IReadOnlyList<string> tableCandidates,
        IReadOnlyList<string> sourceDatasets,
        IReadOnlyList<string> sinkDatasets)
    {
        if (!string.IsNullOrWhiteSpace(storedProcedure)
            || (!string.IsNullOrWhiteSpace(sqlText) && tableCandidates.Count == 0))
        {
            return "Needs Review";
        }

        return tableCandidates.Count > 0 || sourceDatasets.Count > 0 || sinkDatasets.Count > 0
            ? "Needs Review"
            : "Cannot Detect";
    }

    private static string GetInventoryNotes(
        AdfActivitySummary activity,
        string storedProcedure,
        string sqlText,
        IReadOnlyList<string> tableCandidates)
    {
        if (!string.IsNullOrWhiteSpace(storedProcedure))
        {
            return "Review procedure internals, key columns, compare columns, and filter before enabling.";
        }

        if (!string.IsNullOrWhiteSpace(sqlText) && tableCandidates.Count == 0)
        {
            return "SQL text detected, but table was not parsed. Review manually.";
        }

        if (string.Equals(activity.Type, "ExecutePipeline", StringComparison.OrdinalIgnoreCase))
        {
            return "Review child pipeline inventory as well.";
        }

        return "Set KeyColumnsJson, optional CompareColumnsJson, WhereClause/ParametersJson, ReviewStatus='Reviewed', and IsActive=1.";
    }

    private string GetRequiredSetting(string name)
    {
        var value = _configuration[name]
            ?? _configuration[$"AppResources:{name}"];

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is not configured.")
            : value;
    }

    private sealed class InventoryRow
    {
        public string WorkflowName { get; init; } = "";
        public string FolderName { get; init; } = "";
        public string PipelineName { get; init; } = "";
        public string ActivityName { get; init; } = "";
        public string ActivityType { get; init; } = "";
        public string ActivityStatus { get; init; } = "";
        public string DetectedStoredProcedure { get; init; } = "";
        public string DetectedSqlText { get; init; } = "";
        public string DetectedSourceDataset { get; init; } = "";
        public string DetectedSinkDataset { get; init; } = "";
        public string DetectedTableCandidates { get; init; } = "";
        public string SuggestedValidationType { get; init; } = "";
        public string SuggestedValidationTable { get; init; } = "";
        public string ReviewStatus { get; init; } = "";
        public string Notes { get; init; } = "";
    }
}
