using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using IfrFunction.Models;
using IfrFunction.Services.AptToPdf;

namespace IfrFunction.Functions;

public sealed class AptToPdfHttpFunction
{
    private readonly IAptToPdfPipelineService _pipeline;
    private readonly IAptToPdfSchedulerService _scheduler;

    public AptToPdfHttpFunction(
        IAptToPdfPipelineService pipeline,
        IAptToPdfSchedulerService scheduler)
    {
        _pipeline = pipeline;
        _scheduler = scheduler;
    }

    /// <summary>
    /// Manual run of APT-to-PDF pipeline.
    /// POST /api/ifr/apt-to-pdf           — runs immediately
    /// POST /api/ifr/apt-to-pdf?scheduled=true — respects JOB_SCHEDULE due time
    /// </summary>
    [Function("AptToPdf")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ifr/apt-to-pdf")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var useSchedule = req.Query["scheduled"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        AptToPdfPipelineResult result;
        if (useSchedule)
        {
            result = await _scheduler.TryRunScheduledJobAsync(cancellationToken)
                ?? new AptToPdfPipelineResult
                {
                    Succeeded = false,
                    ErrorMessage = "Job is not due, disabled, or already running."
                };
        }
        else
        {
            result = await _pipeline.RunAsync(cancellationToken);
        }

        var status = result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
