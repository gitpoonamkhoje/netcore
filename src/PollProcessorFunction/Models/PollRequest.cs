namespace PollProcessorFunction.Models;

public sealed class PollRequest
{
    public string AppId { get; set; } = "";
    public string Mode { get; set; } = "F";
    public bool Reset { get; set; } = true;
    public int PollForHours { get; set; } = 2;
}

