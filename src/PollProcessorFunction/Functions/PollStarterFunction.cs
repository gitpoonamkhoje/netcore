using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PollProcessorFunction.Configuration;
using PollProcessorFunction.Models;
using PollProcessorFunction.Services;

namespace PollProcessorFunction.Functions;

public sealed class PollStarterFunction
{
    private readonly IEnvironmentResources _resources;
    private readonly IPollProcessRepository _repository;
    private readonly ILogger<PollStarterFunction> _log;

    public PollStarterFunction(
        IEnvironmentResources resources,
        IPollProcessRepository repository,
        ILogger<PollStarterFunction> log)
    {
        _resources = resources;
        _repository = repository;
        _log = log;
    }

    [Function("PollStarter")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        CancellationToken cancellationToken)
    {
        var body = await req.ReadAsStringAsync();

        var input = string.IsNullOrWhiteSpace(body)
            ? new PollRequest()
            : JsonConvert.DeserializeObject<PollRequest>(body) ?? new PollRequest();

        string instanceId =
            await client.ScheduleNewOrchestrationInstanceAsync(
                "PollOrchestrator",
                input,
                cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.Accepted);

        await response.WriteAsJsonAsync(new
        {
            InstanceId = instanceId,
            Status = "Started"
        });

        return response;
    }

    [Function("GetPollStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "poll/{instanceId}")]
        HttpRequestData req,
        string instanceId,
        [DurableClient] DurableTaskClient client,
        CancellationToken cancellationToken)
    {
        var metadata = await client.GetInstanceAsync(instanceId, getInputsAndOutputs: true);

        var response = req.CreateResponse(HttpStatusCode.OK);

        if (metadata == null)
        {
            await response.WriteAsJsonAsync(new { Found = false });
            return response;
        }

        if (metadata.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
        {
            var result = metadata.ReadOutputAs<PollOrchestrationResult>();

            await response.WriteAsJsonAsync(new
            {
                Completed = true,
                Status = "Completed",
                Result = result
            });

            return response;
        }

        if (metadata.RuntimeStatus is OrchestrationRuntimeStatus.Failed
            or OrchestrationRuntimeStatus.Terminated)
        {
            var input = metadata.ReadInputAs<PollRequest>();
            var rowsMarkedFailed = 0;

            if (!string.IsNullOrWhiteSpace(input?.AppId))
            {
                await using var conn = _resources.CreateSqlConnection();
                await conn.OpenAsync(cancellationToken);
                rowsMarkedFailed = await _repository.MarkActiveRowsFailedForAppAsync(
                    conn,
                    input.AppId,
                    cancellationToken);

                _log.LogWarning(
                    "Orchestration {InstanceId} {Status}; marked {Count} active poll_process row(s) as failed for AppId {AppId}",
                    instanceId,
                    metadata.RuntimeStatus,
                    rowsMarkedFailed,
                    input.AppId);
            }

            await response.WriteAsJsonAsync(new
            {
                Completed = true,
                Status = metadata.RuntimeStatus.ToString(),
                Failed = true,
                ErrorMessage = metadata.FailureDetails?.ErrorMessage,
                ActiveRowsMarkedFailed = rowsMarkedFailed,
                Result = metadata.ReadOutputAs<PollOrchestrationResult>()
            });

            return response;
        }

        await response.WriteAsJsonAsync(new
        {
            Completed = false,
            Status = metadata.RuntimeStatus.ToString(),
            CustomStatus = metadata.ReadCustomStatusAs<PollOrchestrationResult>()
        });

        return response;
    }
}
