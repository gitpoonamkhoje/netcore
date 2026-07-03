using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using IfrFunction.Models;
using IfrFunction.Services;

namespace IfrFunction.Functions;

public sealed class AptSpectrHttpFunction
{
    private readonly IAptSpectrService _service;

    public AptSpectrHttpFunction(IAptSpectrService service)
    {
        _service = service;
    }

    /// <summary>
    /// (2) APT and SPECTR — read from Azure File Share, convert to PDF, store metadata in MTB_APT / MTB_SPECTR.
    /// </summary>
    [Function("AptSpectr")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ifr/apt-spectr")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        AptSpectrRequest request;
        try
        {
            var body = await req.ReadAsStringAsync();
            request = string.IsNullOrWhiteSpace(body)
                ? new AptSpectrRequest()
                : JsonConvert.DeserializeObject<AptSpectrRequest>(body) ?? new AptSpectrRequest();
        }
        catch (Exception ex)
        {
            return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { Error = ex.Message }, cancellationToken);
        }

        var result = await _service.ProcessAsync(request, cancellationToken);
        var status = result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
        return await WriteJsonAsync(req, status, result, cancellationToken);
    }

    private static async Task<HttpResponseData> WriteJsonAsync(
        HttpRequestData req,
        HttpStatusCode statusCode,
        object value,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken);
        return response;
    }
}
