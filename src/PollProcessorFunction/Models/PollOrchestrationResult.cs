namespace PollProcessorFunction.Models;

public sealed class PollOrchestrationResult
{
    /// <summary>
    /// All job names handed off for triggering during this orchestration run.
    /// </summary>
    public IReadOnlyList<string> JobsToTrigger { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Ready jobs for external ADF handoff (Id, pipeline name, file, app).
    /// </summary>
    public IReadOnlyList<ReadyJobHandoff> ReadyJobs { get; set; } = Array.Empty<ReadyJobHandoff>();

    public IReadOnlyList<string> TriggeredPipelines { get; set; } = Array.Empty<string>();

    public IReadOnlyList<PipelineTriggerFailure> FailedTriggers { get; set; } = Array.Empty<PipelineTriggerFailure>();
}
