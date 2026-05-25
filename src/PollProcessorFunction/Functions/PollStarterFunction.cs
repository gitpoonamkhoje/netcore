using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Newtonsoft.Json;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Functions;

public sealed class PollStarterFunction
{
    [Function("PollStarter")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var body = await req.ReadAsStringAsync();
        var input = string.IsNullOrWhiteSpace(body)
            ? new PollRequest()
            : JsonConvert.DeserializeObject<PollRequest>(body) ?? new PollRequest();

        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            "PollOrchestrator",
            input);

        return client.CreateCheckStatusResponse(req, instanceId);
    }
}

