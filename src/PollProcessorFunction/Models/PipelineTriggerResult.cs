namespace PollProcessorFunction.Models;

public sealed class PipelineTriggerResult
{
    public IReadOnlyList<string> TriggeredPipelines { get; set; } = Array.Empty<string>();

    public IReadOnlyList<PipelineTriggerFailure> FailedTriggers { get; set; } = Array.Empty<PipelineTriggerFailure>();
}

public sealed class PipelineTriggerFailure
{
    public int Id { get; set; }

    public string? JobName { get; set; }

    public string? FileName { get; set; }

    public string Reason { get; set; } = "";
}
