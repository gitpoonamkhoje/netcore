namespace PollProcessorFunction.Models;

/// <summary>
/// Job ready for external ADF triggering (handoff mode).
/// </summary>
public sealed class ReadyJobHandoff
{
    public int Id { get; set; }

    public string? JobName { get; set; }

    public string? FileName { get; set; }

    public string? AppId { get; set; }
}
