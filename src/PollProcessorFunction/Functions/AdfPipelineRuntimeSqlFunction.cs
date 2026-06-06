using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions;

public sealed class AdfPipelineRuntimeSqlFunction
{
    private static readonly TimeSpan LatestRunLookback = TimeSpan.FromDays(45);

    private readonly IAdfPipelineMetadataService _metadataService;
    private readonly IAdfPipelineRuntimeSqlWriter _sqlWriter;

    public AdfPipelineRuntimeSqlFunction(
        IAdfPipelineMetadataService metadataService,
        IAdfPipelineRuntimeSqlWriter sqlWriter)
    {
        _metadataService = metadataService;
        _sqlWriter = sqlWriter;
    }

    [Function("SaveAdfPipelineRuntimeInfoToSql")]
    public async Task<HttpResponseData> SaveAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "adf/pipelines/runtime/sql")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);
        var latestRuns = await GetLatestPipelineRunsAsync(pipelines, cancellationToken);
        var rows = BuildRows(pipelines, latestRuns);

        var result = await _sqlWriter.SaveAsync(rows, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonConvert.SerializeObject(new
            {
                Status = "Saved",
                TableName = "dbo.AdfPipelineRuntimeInfoResult",
                result.SnapshotId,
                result.CapturedAtUtc,
                result.TotalRowsInserted
            }, Formatting.Indented),
            cancellationToken);

        return response;
    }

    private async Task<IReadOnlyDictionary<string, AdfPipelineRunSummary>> GetLatestPipelineRunsAsync(
        IReadOnlyList<AdfPipelineDetails> pipelines,
        CancellationToken cancellationToken)
    {
        var tasks = pipelines.Select(async pipeline => new
        {
            pipeline.PipelineName,
            Run = await _metadataService.GetLatestPipelineRunAsync(
                pipeline.PipelineName,
                LatestRunLookback,
                cancellationToken)
        });

        var results = await Task.WhenAll(tasks);
        return results
            .Where(result => result.Run is not null)
            .ToDictionary(
                result => result.PipelineName,
                result => result.Run!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AdfPipelineRuntimeInfoRow> BuildRows(
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> latestRuns)
    {
        var rows = new List<AdfPipelineRuntimeInfoRow>();

        foreach (var pipeline in pipelines)
        {
            var folderName = pipeline.Pipeline.SelectToken("properties.folder.name")?.Value<string>() ?? "";
            latestRuns.TryGetValue(pipeline.PipelineName, out var run);

            foreach (var activity in pipeline.Activities)
            {
                rows.Add(new AdfPipelineRuntimeInfoRow
                {
                    FolderName = folderName,
                    PipelineName = pipeline.PipelineName,
                    ObjectName = activity.Name,
                    ObjectCategory = string.Equals(activity.Type, "Script", StringComparison.OrdinalIgnoreCase)
                        ? "Script"
                        : "Activity",
                    ObjectType = activity.Type,
                    Status = activity.Status,
                    Description = activity.Description,
                    LatestPipelineRunStatus = run?.Status ?? "",
                    LatestPipelineRunId = run?.RunId ?? "",
                    LatestPipelineRunStartUtc = run?.RunStartUtc,
                    LatestPipelineRunEndUtc = run?.RunEndUtc,
                    ExactTimeTakenSeconds = run?.DurationSeconds,
                    ExactTimeTaken = FormatDuration(run?.DurationSeconds)
                });
            }

            foreach (var trigger in pipeline.Triggers)
            {
                rows.Add(new AdfPipelineRuntimeInfoRow
                {
                    FolderName = folderName,
                    PipelineName = pipeline.PipelineName,
                    ObjectName = trigger.Value<string>("name") ?? "",
                    ObjectCategory = "Trigger",
                    ObjectType = trigger.SelectToken("properties.type")?.Value<string>() ?? "",
                    Status = GetTriggerStatus(trigger),
                    Description = trigger.SelectToken("properties.description")?.Value<string>() ?? "",
                    LatestPipelineRunStatus = run?.Status ?? "",
                    LatestPipelineRunId = run?.RunId ?? "",
                    LatestPipelineRunStartUtc = run?.RunStartUtc,
                    LatestPipelineRunEndUtc = run?.RunEndUtc,
                    ExactTimeTakenSeconds = run?.DurationSeconds,
                    ExactTimeTaken = FormatDuration(run?.DurationSeconds)
                });
            }
        }

        return rows;
    }

    private static string GetTriggerStatus(JToken trigger)
    {
        var runtimeState = trigger.SelectToken("properties.runtimeState")?.Value<string>();
        return string.Equals(runtimeState, "Started", StringComparison.OrdinalIgnoreCase)
            ? "Active"
            : "Inactive";
    }

    private static string FormatDuration(double? durationSeconds)
    {
        if (durationSeconds is null)
        {
            return "";
        }

        return TimeSpan.FromSeconds(durationSeconds.Value).ToString(@"hh\:mm\:ss");
    }
}
