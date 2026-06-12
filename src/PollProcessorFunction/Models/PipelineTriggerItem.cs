namespace PollProcessorFunction.Models;

public sealed class PipelineTriggerItem
{
    public int Id { get; set; }

    public string? JobName { get; set; }

    public string? FileName { get; set; }
}
