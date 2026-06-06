using System.Net;
using ClosedXML.Excel;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions;

public sealed class AdfPipelineInfoFunction
{
    private readonly IAdfPipelineMetadataService _metadataService;
    private readonly IAdfPipelineInfoSqlWriter _sqlWriter;

    public AdfPipelineInfoFunction(
        IAdfPipelineMetadataService metadataService,
        IAdfPipelineInfoSqlWriter sqlWriter)
    {
        _metadataService = metadataService;
        _sqlWriter = sqlWriter;
    }

    [Function("GetAdfPipelineInfo")]
    public async Task<HttpResponseData> GetAllAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "adf/pipelines/info")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await WriteJsonAsync(response, new
        {
            Count = pipelines.Count,
            Pipelines = pipelines
        }, cancellationToken);

        return response;
    }

    [Function("SaveAdfPipelineInfoToSql")]
    public async Task<HttpResponseData> SaveToSqlAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "adf/pipelines/info/sql")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);
        var result = await _sqlWriter.SaveAsync(pipelines, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await WriteJsonAsync(response, new
        {
            Status = "Saved",
            TableName = "dbo.AdfPipelineInfoResult",
            result.SnapshotId,
            result.CapturedAtUtc,
            result.PipelineCount,
            result.ActivityCount,
            result.ScriptActivityCount,
            result.ReferenceCount,
            result.TriggerCount,
            result.TotalRowsInserted
        }, cancellationToken);

        return response;
    }

    [Function("DownloadAdfPipelineInfoExcel")]
    public async Task<HttpResponseData> DownloadExcelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "adf/pipelines/info/excel")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);

        using var workbook = BuildWorkbook(pipelines);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition", "attachment; filename=\"adf-pipeline-info.xlsx\"");

        stream.Position = 0;
        await stream.CopyToAsync(response.Body, cancellationToken);

        return response;
    }

    [Function("DownloadAdfPipelineRuntimeExcel")]
    public async Task<HttpResponseData> DownloadRuntimeExcelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "adf/pipelines/runtime/excel")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);
        var pipelineRuns = await GetLatestPipelineRunsAsync(pipelines, cancellationToken);

        using var workbook = BuildRuntimeWorkbook(pipelines, pipelineRuns);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition", "attachment; filename=\"adf-pipeline-runtime-info.xlsx\"");

        stream.Position = 0;
        await stream.CopyToAsync(response.Body, cancellationToken);

        return response;
    }

    [Function("DownloadAdfPipelineReportExcel")]
    public async Task<HttpResponseData> DownloadReportExcelAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "adf/report/excel")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var pipelines = await _metadataService.GetAllPipelineDetailsAsync(cancellationToken);
        var pipelineRuns = await GetLatestPipelineRunsAsync(pipelines, cancellationToken);

        using var workbook = BuildAdfReportWorkbook(pipelines, pipelineRuns);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition", "attachment; filename=\"adf-full-report.xlsx\"");

        stream.Position = 0;
        await stream.CopyToAsync(response.Body, cancellationToken);

        return response;
    }

    [Function("GetAdfPipelineInfoByName")]
    public async Task<HttpResponseData> GetByNameAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "adf/pipelines/{pipelineName}/info")]
        HttpRequestData req,
        string pipelineName,
        CancellationToken cancellationToken)
    {
        var pipeline = await _metadataService.GetPipelineDetailsAsync(pipelineName, cancellationToken);
        if (pipeline is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await WriteJsonAsync(notFound, new
            {
                Found = false,
                PipelineName = pipelineName
            }, cancellationToken);

            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await WriteJsonAsync(response, pipeline, cancellationToken);

        return response;
    }

    private static async Task WriteJsonAsync(
        HttpResponseData response,
        object value,
        CancellationToken cancellationToken)
    {
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonConvert.SerializeObject(value, Formatting.Indented),
            cancellationToken);
    }

    private static XLWorkbook BuildWorkbook(IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var workbook = new XLWorkbook();

        AddPipelinesSheet(workbook, pipelines);
        AddActivitiesSheet(workbook, pipelines);
        AddScriptsSheet(workbook, pipelines);
        AddReferencesSheet(workbook, pipelines);
        AddTriggersSheet(workbook, pipelines);

        return workbook;
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
                TimeSpan.FromDays(45),
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

    private static XLWorkbook BuildRuntimeWorkbook(
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Pipeline Runtime");
        WriteHeader(
            sheet,
            "Folder Name",
            "Pipeline Name",
            "Activity/Script/Trigger Name",
            "Object Category",
            "Type",
            "Status",
            "Description",
            "Latest Pipeline Run Status",
            "Latest Pipeline Run Id",
            "Latest Pipeline Run Start Utc",
            "Latest Pipeline Run End Utc",
            "Exact Time Taken Seconds",
            "Exact Time Taken");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            var folderName = pipeline.Pipeline.SelectToken("properties.folder.name")?.Value<string>() ?? "";
            pipelineRuns.TryGetValue(pipeline.PipelineName, out var run);

            foreach (var activity in pipeline.Activities)
            {
                AddRuntimeRow(
                    sheet,
                    row++,
                    folderName,
                    pipeline.PipelineName,
                    activity.Name,
                    string.Equals(activity.Type, "Script", StringComparison.OrdinalIgnoreCase)
                        ? "Script"
                        : "Activity",
                    activity.Type,
                    activity.Status,
                    activity.Description,
                    run);
            }

            foreach (var trigger in pipeline.Triggers)
            {
                AddRuntimeRow(
                    sheet,
                    row++,
                    folderName,
                    pipeline.PipelineName,
                    trigger.Value<string>("name") ?? "",
                    "Trigger",
                    trigger.SelectToken("properties.type")?.Value<string>() ?? "",
                    GetTriggerStatus(trigger),
                    trigger.SelectToken("properties.description")?.Value<string>() ?? "",
                    run);
            }
        }

        FormatSheet(sheet);
        return workbook;
    }

    private static XLWorkbook BuildAdfReportWorkbook(
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var workbook = new XLWorkbook();

        AddReportSummarySheet(workbook, pipelines, pipelineRuns);
        AddReportPipelinesSheet(workbook, pipelines, pipelineRuns);
        AddReportActivitiesSheet(workbook, pipelines, pipelineRuns);
        AddReportScriptsSheet(workbook, pipelines, pipelineRuns);
        AddReportTriggersSheet(workbook, pipelines, pipelineRuns);
        AddReportReferencesSheet(workbook, pipelines);
        AddReportRuntimeSheet(workbook, pipelines, pipelineRuns);

        return workbook;
    }

    private static void AddReportSummarySheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var sheet = workbook.Worksheets.Add("Summary");
        WriteHeader(sheet, "Metric", "Value");

        sheet.Cell(2, 1).Value = "Generated Utc";
        sheet.Cell(2, 2).Value = DateTime.UtcNow.ToString("O");
        sheet.Cell(3, 1).Value = "Pipeline Count";
        sheet.Cell(3, 2).Value = pipelines.Count;
        sheet.Cell(4, 1).Value = "Activity Count";
        sheet.Cell(4, 2).Value = pipelines.Sum(pipeline => pipeline.Activities.Count);
        sheet.Cell(5, 1).Value = "Script Activity Count";
        sheet.Cell(5, 2).Value = pipelines.Sum(pipeline => pipeline.ScriptActivities.Count);
        sheet.Cell(6, 1).Value = "Trigger Count";
        sheet.Cell(6, 2).Value = pipelines.Sum(pipeline => pipeline.Triggers.Count);
        sheet.Cell(7, 1).Value = "Linked Reference Count";
        sheet.Cell(7, 2).Value = pipelines.Sum(pipeline => pipeline.ReferencedResources.Count);
        sheet.Cell(8, 1).Value = "Pipelines With Latest Run";
        sheet.Cell(8, 2).Value = pipelineRuns.Count;

        FormatSheet(sheet);
    }

    private static void AddReportPipelinesSheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var sheet = workbook.Worksheets.Add("Pipelines");
        WriteHeader(
            sheet,
            "Folder Name",
            "Pipeline Name",
            "Latest Run Status",
            "Latest Run Id",
            "Run Start Utc",
            "Run End Utc",
            "Duration Seconds",
            "Duration",
            "Pipeline Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            pipelineRuns.TryGetValue(pipeline.PipelineName, out var run);

            sheet.Cell(row, 1).Value = GetFolderName(pipeline);
            sheet.Cell(row, 2).Value = pipeline.PipelineName;
            sheet.Cell(row, 3).Value = run?.Status ?? "";
            sheet.Cell(row, 4).Value = run?.RunId ?? "";
            sheet.Cell(row, 5).Value = run?.RunStartUtc?.ToString("O") ?? "";
            sheet.Cell(row, 6).Value = run?.RunEndUtc?.ToString("O") ?? "";
            sheet.Cell(row, 7).Value = run?.DurationSeconds is null ? "" : run.DurationSeconds.Value;
            sheet.Cell(row, 8).Value = FormatDuration(run?.DurationSeconds);
            sheet.Cell(row, 9).Value = ToJson(pipeline.Pipeline);
            row++;
        }

        FormatSheet(sheet);
    }

    private static void AddReportActivitiesSheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var sheet = workbook.Worksheets.Add("Activities");
        WriteHeader(
            sheet,
            "Folder Name",
            "Pipeline Name",
            "Activity Name",
            "Activity Type",
            "Status",
            "Description",
            "Latest Pipeline Run Status",
            "Duration Seconds",
            "Duration",
            "Type Properties Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            pipelineRuns.TryGetValue(pipeline.PipelineName, out var run);

            foreach (var activity in pipeline.Activities)
            {
                sheet.Cell(row, 1).Value = GetFolderName(pipeline);
                sheet.Cell(row, 2).Value = pipeline.PipelineName;
                sheet.Cell(row, 3).Value = activity.Name;
                sheet.Cell(row, 4).Value = activity.Type;
                sheet.Cell(row, 5).Value = activity.Status;
                sheet.Cell(row, 6).Value = activity.Description;
                sheet.Cell(row, 7).Value = run?.Status ?? "";
                sheet.Cell(row, 8).Value = run?.DurationSeconds is null ? "" : run.DurationSeconds.Value;
                sheet.Cell(row, 9).Value = FormatDuration(run?.DurationSeconds);
                sheet.Cell(row, 10).Value = ToJson(activity.TypeProperties);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddReportScriptsSheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var sheet = workbook.Worksheets.Add("Scripts");
        WriteHeader(
            sheet,
            "Folder Name",
            "Pipeline Name",
            "Script Activity Name",
            "Activity Type",
            "Status",
            "Description",
            "Latest Pipeline Run Status",
            "Duration Seconds",
            "Duration",
            "Scripts Json",
            "Type Properties Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            pipelineRuns.TryGetValue(pipeline.PipelineName, out var run);

            foreach (var activity in pipeline.ScriptActivities)
            {
                sheet.Cell(row, 1).Value = GetFolderName(pipeline);
                sheet.Cell(row, 2).Value = pipeline.PipelineName;
                sheet.Cell(row, 3).Value = activity.Name;
                sheet.Cell(row, 4).Value = activity.Type;
                sheet.Cell(row, 5).Value = activity.Status;
                sheet.Cell(row, 6).Value = activity.Description;
                sheet.Cell(row, 7).Value = run?.Status ?? "";
                sheet.Cell(row, 8).Value = run?.DurationSeconds is null ? "" : run.DurationSeconds.Value;
                sheet.Cell(row, 9).Value = FormatDuration(run?.DurationSeconds);
                sheet.Cell(row, 10).Value = ToJson(activity.Scripts);
                sheet.Cell(row, 11).Value = ToJson(activity.TypeProperties);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddReportTriggersSheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var sheet = workbook.Worksheets.Add("Triggers");
        WriteHeader(
            sheet,
            "Folder Name",
            "Pipeline Name",
            "Trigger Name",
            "Trigger Type",
            "Status",
            "Description",
            "Latest Pipeline Run Status",
            "Duration Seconds",
            "Duration",
            "Trigger Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            pipelineRuns.TryGetValue(pipeline.PipelineName, out var run);

            foreach (var trigger in pipeline.Triggers)
            {
                sheet.Cell(row, 1).Value = GetFolderName(pipeline);
                sheet.Cell(row, 2).Value = pipeline.PipelineName;
                sheet.Cell(row, 3).Value = trigger.Value<string>("name") ?? "";
                sheet.Cell(row, 4).Value = trigger.SelectToken("properties.type")?.Value<string>() ?? "";
                sheet.Cell(row, 5).Value = GetTriggerStatus(trigger);
                sheet.Cell(row, 6).Value = trigger.SelectToken("properties.description")?.Value<string>() ?? "";
                sheet.Cell(row, 7).Value = run?.Status ?? "";
                sheet.Cell(row, 8).Value = run?.DurationSeconds is null ? "" : run.DurationSeconds.Value;
                sheet.Cell(row, 9).Value = FormatDuration(run?.DurationSeconds);
                sheet.Cell(row, 10).Value = ToJson(trigger);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddReportReferencesSheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("Linked References");
        WriteHeader(sheet, "Folder Name", "Pipeline Name", "Reference Name", "Reference Type", "Json Path");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            foreach (var reference in pipeline.ReferencedResources)
            {
                sheet.Cell(row, 1).Value = GetFolderName(pipeline);
                sheet.Cell(row, 2).Value = pipeline.PipelineName;
                sheet.Cell(row, 3).Value = reference.ReferenceName;
                sheet.Cell(row, 4).Value = reference.ReferenceType;
                sheet.Cell(row, 5).Value = reference.Path;
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddReportRuntimeSheet(
        XLWorkbook workbook,
        IReadOnlyList<AdfPipelineDetails> pipelines,
        IReadOnlyDictionary<string, AdfPipelineRunSummary> pipelineRuns)
    {
        var sheet = workbook.Worksheets.Add("Runtime");
        WriteHeader(
            sheet,
            "Folder Name",
            "Pipeline Name",
            "Run Status",
            "Run Id",
            "Run Start Utc",
            "Run End Utc",
            "Duration Seconds",
            "Duration",
            "Raw Run Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            pipelineRuns.TryGetValue(pipeline.PipelineName, out var run);

            sheet.Cell(row, 1).Value = GetFolderName(pipeline);
            sheet.Cell(row, 2).Value = pipeline.PipelineName;
            sheet.Cell(row, 3).Value = run?.Status ?? "";
            sheet.Cell(row, 4).Value = run?.RunId ?? "";
            sheet.Cell(row, 5).Value = run?.RunStartUtc?.ToString("O") ?? "";
            sheet.Cell(row, 6).Value = run?.RunEndUtc?.ToString("O") ?? "";
            sheet.Cell(row, 7).Value = run?.DurationSeconds is null ? "" : run.DurationSeconds.Value;
            sheet.Cell(row, 8).Value = FormatDuration(run?.DurationSeconds);
            sheet.Cell(row, 9).Value = ToJson(run?.RawRun);
            row++;
        }

        FormatSheet(sheet);
    }

    private static void AddRuntimeRow(
        IXLWorksheet sheet,
        int row,
        string folderName,
        string pipelineName,
        string objectName,
        string objectCategory,
        string objectType,
        string status,
        string description,
        AdfPipelineRunSummary? run)
    {
        sheet.Cell(row, 1).Value = folderName;
        sheet.Cell(row, 2).Value = pipelineName;
        sheet.Cell(row, 3).Value = objectName;
        sheet.Cell(row, 4).Value = objectCategory;
        sheet.Cell(row, 5).Value = objectType;
        sheet.Cell(row, 6).Value = status;
        sheet.Cell(row, 7).Value = description;
        sheet.Cell(row, 8).Value = run?.Status ?? "";
        sheet.Cell(row, 9).Value = run?.RunId ?? "";
        sheet.Cell(row, 10).Value = run?.RunStartUtc?.ToString("O") ?? "";
        sheet.Cell(row, 11).Value = run?.RunEndUtc?.ToString("O") ?? "";
        sheet.Cell(row, 12).Value = run?.DurationSeconds is null
            ? ""
            : run.DurationSeconds.Value;
        sheet.Cell(row, 13).Value = FormatDuration(run?.DurationSeconds);
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

    private static void AddPipelinesSheet(XLWorkbook workbook, IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("Pipelines");
        WriteHeader(sheet, "Pipeline Name", "Pipeline Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            sheet.Cell(row, 1).Value = pipeline.PipelineName;
            sheet.Cell(row, 2).Value = ToJson(pipeline.Pipeline);
            row++;
        }

        FormatSheet(sheet);
    }

    private static void AddActivitiesSheet(XLWorkbook workbook, IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("Activities");
        WriteHeader(
            sheet,
            "Pipeline Name",
            "Activity Name",
            "Activity Type",
            "Activity Status",
            "Activity Description",
            "Depends On Json",
            "Inputs Json",
            "Outputs Json",
            "Type Properties Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            foreach (var activity in pipeline.Activities)
            {
                sheet.Cell(row, 1).Value = pipeline.PipelineName;
                sheet.Cell(row, 2).Value = activity.Name;
                sheet.Cell(row, 3).Value = activity.Type;
                sheet.Cell(row, 4).Value = activity.Status;
                sheet.Cell(row, 5).Value = activity.Description;
                sheet.Cell(row, 6).Value = ToJson(activity.DependsOn);
                sheet.Cell(row, 7).Value = ToJson(activity.Inputs);
                sheet.Cell(row, 8).Value = ToJson(activity.Outputs);
                sheet.Cell(row, 9).Value = ToJson(activity.TypeProperties);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddScriptsSheet(XLWorkbook workbook, IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("Script Activities");
        WriteHeader(
            sheet,
            "Pipeline Name",
            "Activity Name",
            "Activity Type",
            "Activity Status",
            "Activity Description",
            "Scripts Json",
            "Type Properties Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            foreach (var activity in pipeline.ScriptActivities)
            {
                sheet.Cell(row, 1).Value = pipeline.PipelineName;
                sheet.Cell(row, 2).Value = activity.Name;
                sheet.Cell(row, 3).Value = activity.Type;
                sheet.Cell(row, 4).Value = activity.Status;
                sheet.Cell(row, 5).Value = activity.Description;
                sheet.Cell(row, 6).Value = ToJson(activity.Scripts);
                sheet.Cell(row, 7).Value = ToJson(activity.TypeProperties);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddReferencesSheet(XLWorkbook workbook, IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("References");
        WriteHeader(sheet, "Pipeline Name", "Reference Name", "Reference Type", "Json Path");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            foreach (var reference in pipeline.ReferencedResources)
            {
                sheet.Cell(row, 1).Value = pipeline.PipelineName;
                sheet.Cell(row, 2).Value = reference.ReferenceName;
                sheet.Cell(row, 3).Value = reference.ReferenceType;
                sheet.Cell(row, 4).Value = reference.Path;
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddTriggersSheet(XLWorkbook workbook, IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("Triggers");
        WriteHeader(sheet, "Pipeline Name", "Trigger Name", "Trigger Type", "Trigger Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            foreach (var trigger in pipeline.Triggers)
            {
                sheet.Cell(row, 1).Value = pipeline.PipelineName;
                sheet.Cell(row, 2).Value = trigger.Value<string>("name") ?? "";
                sheet.Cell(row, 3).Value = trigger.SelectToken("properties.type")?.Value<string>() ?? "";
                sheet.Cell(row, 4).Value = ToJson(trigger);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void WriteHeader(IXLWorksheet sheet, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }
    }

    private static void FormatSheet(IXLWorksheet sheet)
    {
        var usedRange = sheet.RangeUsed();
        if (usedRange is null)
        {
            return;
        }

        usedRange.Style.Alignment.WrapText = true;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.SheetView.FreezeRows(1);
        usedRange.SetAutoFilter();
        sheet.Columns().AdjustToContents(1, 60);
    }

    private static string ToJson(JToken? token)
    {
        return token is null
            ? ""
            : token.ToString(Formatting.Indented);
    }

    private static string GetFolderName(AdfPipelineDetails pipeline)
    {
        return pipeline.Pipeline.SelectToken("properties.folder.name")?.Value<string>() ?? "";
    }
}
