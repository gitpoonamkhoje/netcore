using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Newtonsoft.Json;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Functions;

public sealed class PollStarterFunction
{
    //[Function("PollStarter")]
    //public async Task<HttpResponseData> StartAsync(
    //    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
    //    [DurableClient] DurableTaskClient client,
    //    CancellationToken cancellationToken)
    //{
    //    var body = await req.ReadAsStringAsync();
    //    var input = string.IsNullOrWhiteSpace(body)
    //        ? new PollRequest()
    //        : JsonConvert.DeserializeObject<PollRequest>(body) ?? new PollRequest();

    //    string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
    //        "PollOrchestrator",
    //        input,
    //        cancellation: cancellationToken);

    //    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    //    timeoutCts.CancelAfter(TimeSpan.FromHours(input.PollForHours) + TimeSpan.FromMinutes(10));

    //    var metadata = await client.WaitForInstanceCompletionAsync(
    //        instanceId,
    //        getInputsAndOutputs: true,
    //        cancellation: timeoutCts.Token);

    //    var result = metadata.ReadOutputAs<PollOrchestrationResult>()
    //        ?? new PollOrchestrationResult();

    //    var response = req.CreateResponse(HttpStatusCode.OK);
    //    await response.WriteAsJsonAsync(result, cancellationToken);
    //    return response;
    //}

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
    [DurableClient] DurableTaskClient client)
{
    var metadata = await client.GetInstanceAsync(instanceId);

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
            Result = result
        });

        return response;
    }

    await response.WriteAsJsonAsync(new
    {
        Completed = false,
        Status = metadata.RuntimeStatus.ToString()
    });

    return response;
}


}
