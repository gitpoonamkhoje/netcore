namespace IfrFunction.Models;

public sealed class AptSpectrRequest
{
    /// <summary>APT or SPECTR</summary>
    public string Source { get; set; } = AptSpectrSources.Apt;

    /// <summary>Relative path on the file share, e.g. apt/inbound/myfile.txt</summary>
    public string? InputSharePath { get; set; }

    /// <summary>When set, process every file in this share directory (non-recursive).</summary>
    public string? InputDirectory { get; set; }

    /// <summary>Diagram note: logo, bold, underline — used for SPECTR path.</summary>
    public bool ApplyTransformations { get; set; }

    /// <summary>When true, skip files that already have a completed metadata row.</summary>
    public bool SkipAlreadyProcessed { get; set; }

    /// <summary>Move source file to processed folder after success. Null uses AppResources.MoveProcessedFiles.</summary>
    public bool? MoveToProcessedFolder { get; set; }
}
