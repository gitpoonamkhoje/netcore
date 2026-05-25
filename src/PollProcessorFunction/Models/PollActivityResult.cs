namespace PollProcessorFunction.Models;

public sealed class PollActivityResult
{
    /// <summary>
    /// True when there are no waiting poll_process rows (orchestrator may stop polling).
    /// </summary>
    public bool NoWaitingItems { get; set; }

    /// <summary>
    /// Job names ready to trigger (condition met); returned to the orchestrator while polling continues.
    /// </summary>
    public IReadOnlyList<string> JobsToTrigger { get; set; } = Array.Empty<string>();
}
