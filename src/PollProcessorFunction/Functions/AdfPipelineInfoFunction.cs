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
                sheet.Cell(row, 4).Value = ToJson(activity.DependsOn);
                sheet.Cell(row, 5).Value = ToJson(activity.Inputs);
                sheet.Cell(row, 6).Value = ToJson(activity.Outputs);
                sheet.Cell(row, 7).Value = ToJson(activity.TypeProperties);
                row++;
            }
        }

        FormatSheet(sheet);
    }

    private static void AddScriptsSheet(XLWorkbook workbook, IReadOnlyList<AdfPipelineDetails> pipelines)
    {
        var sheet = workbook.Worksheets.Add("Script Activities");
        WriteHeader(sheet, "Pipeline Name", "Activity Name", "Activity Type", "Scripts Json", "Type Properties Json");

        var row = 2;
        foreach (var pipeline in pipelines)
        {
            foreach (var activity in pipeline.ScriptActivities)
            {
                sheet.Cell(row, 1).Value = pipeline.PipelineName;
                sheet.Cell(row, 2).Value = activity.Name;
                sheet.Cell(row, 3).Value = activity.Type;
                sheet.Cell(row, 4).Value = ToJson(activity.Scripts);
                sheet.Cell(row, 5).Value = ToJson(activity.TypeProperties);
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
}
