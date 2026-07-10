namespace IfrFunction.Models;

public static class AptToPdfContainers
{
    public const string Input = "input";
    public const string Processing = "processing";
    public const string Output = "output";
    public const string Archive = "archive";
    public const string Failed = "failed";
    public const string Logs = "logs";
}

public static class AptToPdfJobNames
{
    public const string AptToPdf = "APT_TO_PDF";
}

public static class AptToPdfStatuses
{
    public const string Idle = "Idle";
    public const string Running = "Running";
    public const string Complete = "Complete";
    public const string Failed = "Failed";
}

public static class AptToPdfBlobMetadata
{
    public const string RetryCount = "aptretrycount";
    public const string SourceContainer = "aptsourcecontainer";
    public const string OriginalFileName = "aptoriginalfilename";
}

public sealed class AptToPdfJobSchedule
{
    public required string JobName { get; init; }
    public required string Frequency { get; init; }
    public TimeSpan? RunTime { get; init; }
    public DateTime NextRunTimeUtc { get; init; }
    public bool Enabled { get; init; }
    public string Status { get; init; } = AptToPdfStatuses.Idle;
}

public sealed class AptToPdfFileResult
{
    public required string FileName { get; init; }
    public bool Succeeded { get; init; }
    public int Attempts { get; init; }
    public string? OutputBlobName { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AptToPdfPipelineResult
{
    public bool Succeeded { get; init; }
    public int TotalFiles { get; init; }
    public int SucceededFiles { get; init; }
    public int FailedFiles { get; init; }
    public IReadOnlyList<AptToPdfFileResult> Files { get; init; } = Array.Empty<AptToPdfFileResult>();
    public IReadOnlyList<string> LogMessages { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }
}
