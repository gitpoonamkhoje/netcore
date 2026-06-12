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
        var triggeredPipelines = new List<string>();
        var failedTriggers = new List<PipelineTriggerFailure>();

        while (context.CurrentUtcDateTime < stopTime)
        {
            var pollResult = await context.CallActivityAsync<PollActivityResult>(
                "PollActivity",
                input);

            if (pollResult.JobsToTrigger.Count > 0)
            {
                jobsToTrigger.AddRange(pollResult.JobsToTrigger);
                var triggerResult = await context.CallActivityAsync<PipelineTriggerResult>(
                    "TriggerReadyJobsActivity",
                    pollResult.PipelineTriggers);

                if (triggerResult.TriggeredPipelines.Count > 0)
                {
                    triggeredPipelines.AddRange(triggerResult.TriggeredPipelines);
                }

                if (triggerResult.FailedTriggers.Count > 0)
                {
                    failedTriggers.AddRange(triggerResult.FailedTriggers);
                }

                context.SetCustomStatus(new PollOrchestrationResult
                {
                    JobsToTrigger = jobsToTrigger,
                    TriggeredPipelines = triggeredPipelines,
                    FailedTriggers = failedTriggers
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

        return new PollOrchestrationResult
        {
            JobsToTrigger = jobsToTrigger,
            TriggeredPipelines = triggeredPipelines,
            FailedTriggers = failedTriggers
        };
    }
}

