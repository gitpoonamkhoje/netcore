using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Functions;

public static class PollOrchestrator
{
    [Function("PollOrchestrator")]
    public static async Task<PollOrchestrationResult> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<PollRequest>() ?? new PollRequest();

        bool isRetry = await context.CallActivityAsync<bool>(
            "DetectRetryActivity",
            input);

        if (isRetry)
        {
            await context.CallActivityAsync(
                "FixIncompleteActivity",
                input);
        }

        bool shouldReset = input.Reset;
        if (shouldReset && !isRetry)
        {
            await context.CallActivityAsync(
                "ResetStatusActivity",
                input);
        }

        DateTime stopTime = context.CurrentUtcDateTime.AddHours(input.PollForHours);
        var jobsToTrigger = new List<string>();

        while (context.CurrentUtcDateTime < stopTime)
        {
            var pollResult = await context.CallActivityAsync<PollActivityResult>(
                "PollActivity",
                input);

            if (pollResult.JobsToTrigger.Count > 0)
            {
                jobsToTrigger.AddRange(pollResult.JobsToTrigger);
                context.SetCustomStatus(new PollOrchestrationResult
                {
                    JobsToTrigger = jobsToTrigger
                });
            }

            if (pollResult.NoWaitingItems)
            {
                break;
            }

            await context.CreateTimer(
                context.CurrentUtcDateTime.AddSeconds(30),
                CancellationToken.None);
        }

        await context.CallActivityAsync(
            "FinalizeActivity",
            input);

        return new PollOrchestrationResult { JobsToTrigger = jobsToTrigger };
    }
}

