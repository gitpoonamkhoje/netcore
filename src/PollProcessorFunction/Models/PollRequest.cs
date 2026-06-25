namespace PollProcessorFunction.Models;

public sealed class PollRequest
{
    public string AppId { get; set; } = "";
    public string Mode { get; set; } = "F";
    public bool Reset { get; set; } = true;

    /// <summary>
    /// ADF retry / restart signal (replaces old @SPIDNo + SQL Agent retry check).
    /// When true: skip reset and keep existing A rows (old @reset = 'N' on retry).
    /// </summary>
    public bool IsRetry { get; set; }

    /// <summary>
    /// How long to poll (hours). Supports fractions, e.g. 0.25 = 15 minutes.
    /// </summary>
    public double PollForHours { get; set; } = 2;
}

