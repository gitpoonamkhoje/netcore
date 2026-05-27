namespace PollProcessorFunction.Models;

public sealed class PollRequest
{
    public string AppId { get; set; } = "";
    public string Mode { get; set; } = "F";
    public bool Reset { get; set; } = true;
    /// <summary>
    /// How long to poll (hours). Supports fractions, e.g. 0.25 = 15 minutes.
    /// </summary>
    public double PollForHours { get; set; } = 2;
}

