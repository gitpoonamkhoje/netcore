namespace PollProcessorFunction.Models;

public sealed class PollItem
{
    public int Id { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? JobName { get; set; }
    public string? Mode { get; set; }
    public string? SqlQuery { get; set; }
    public object? ExpectedValue { get; set; }
    public DateTime? LastRun { get; set; }
    public int? AgeThreshold { get; set; }
    public string? AgeJobName { get; set; }
    public string? RemainJobName { get; set; }
}

