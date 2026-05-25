namespace PollProcessorFunction.Models;

public sealed class PollOrchestrationResult
{
    /// <summary>
    /// All job names handed off for triggering during this orchestration run.
    /// </summary>
    public IReadOnlyList<string> JobsToTrigger { get; set; } = Array.Empty<string>();
}
