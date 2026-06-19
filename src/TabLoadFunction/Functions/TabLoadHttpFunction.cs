using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using TabLoadFunction.Models;
using TabLoadFunction.Services;

namespace TabLoadFunction.Functions;

public sealed class TabLoadHttpFunction
{
    private readonly ITabLoadService _tabLoadService;

    public TabLoadHttpFunction(ITabLoadService tabLoadService)
    {
        _tabLoadService = tabLoadService;
    }

    [Function("TabLoad")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "tabload")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        TabLoadRequest request;
        try
        {
            request = await ReadRequestAsync(req);
        }
        catch (InvalidOperationException ex)
        {
            return await WriteJsonAsync(
                req,
                HttpStatusCode.BadRequest,
                new { Error = ex.Message },
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.TableName))
        {
            return await WriteJsonAsync(
                req,
                HttpStatusCode.BadRequest,
                new { Error = "TableName is required." },
                cancellationToken);
        }

        var result = await _tabLoadService.LoadAsync(request, cancellationToken);
        return await WriteJsonAsync(
            req,
            result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.InternalServerError,
            result,
            cancellationToken);
    }

    private static async Task<TabLoadRequest> ReadRequestAsync(HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Request body is required.");
        }

        var request = JsonConvert.DeserializeObject<TabLoadRequest>(body);
        return request ?? throw new InvalidOperationException("Request body is invalid.");
    }

    private static async Task<HttpResponseData> WriteJsonAsync(
        HttpRequestData req,
        HttpStatusCode statusCode,
        object value,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonConvert.SerializeObject(value, Formatting.Indented),
            cancellationToken);
        return response;
    }
}
