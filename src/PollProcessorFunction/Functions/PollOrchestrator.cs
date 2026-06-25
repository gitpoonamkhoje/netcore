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

        // Old sp_wtc_pollproc: SQL Agent retry forced @reset = 'N' and kept A rows.
        // Pass IsRetry=true from ADF on pipeline retry (replaces @SPIDNo / @@SPID check).
        if (!input.IsRetry)
        {
            bool hasActiveRows = await context.CallActivityAsync<bool>(
                "DetectRetryActivity",
                input);

            if (hasActiveRows)
            {
                await context.CallActivityAsync(
                    "FixIncompleteActivity",
                    input);
            }

            if (input.Reset)
            {
                await context.CallActivityAsync(
                    "ResetStatusActivity",
                    input);
            }
        }

        DateTime stopTime = context.CurrentUtcDateTime.AddHours(input.PollForHours);
        var jobsToTrigger = new List<string>();
        var readyJobs = new List<ReadyJobHandoff>();
        var triggeredPipelines = new List<string>();
        var failedTriggers = new List<PipelineTriggerFailure>();
        var handedOffIds = new HashSet<int>();

        while (context.CurrentUtcDateTime < stopTime)
        {
            var pollResult = await context.CallActivityAsync<PollActivityResult>(
                "PollActivity",
                input);

            if (pollResult.JobsToTrigger.Count > 0)
            {
                jobsToTrigger.AddRange(pollResult.JobsToTrigger);

                foreach (var item in pollResult.PipelineTriggers)
                {
                    if (handedOffIds.Add(item.Id))
                    {
                        readyJobs.Add(new ReadyJobHandoff
                        {
                            Id = item.Id,
                            JobName = item.JobName,
                            FileName = item.FileName,
                            AppId = input.AppId
                        });
                    }
                }

                // ADF trigger disabled — ADF team triggers pipelines externally from ReadyJobs.
                //var triggerResult = await context.CallActivityAsync<PipelineTriggerResult>(
                //    "TriggerReadyJobsActivity",
                //    pollResult.PipelineTriggers);
                //
                //if (triggerResult.TriggeredPipelines.Count > 0)
                //{
                //    triggeredPipelines.AddRange(triggerResult.TriggeredPipelines);
                //}
                //
                //if (triggerResult.FailedTriggers.Count > 0)
                //{
                //    failedTriggers.AddRange(triggerResult.FailedTriggers);
                //}

                context.SetCustomStatus(new PollOrchestrationResult
                {
                    JobsToTrigger = jobsToTrigger,
                    ReadyJobs = readyJobs,
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
            ReadyJobs = readyJobs,
            TriggeredPipelines = triggeredPipelines,
            FailedTriggers = failedTriggers
        };
    }
}
