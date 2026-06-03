using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions;

public sealed class AdfPipelineInfoFunction
{
    private readonly IAdfPipelineMetadataService _metadataService;

    public AdfPipelineInfoFunction(IAdfPipelineMetadataService metadataService)
    {
        _metadataService = metadataService;
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
}
