namespace PollProcessorFunction.Models;

public sealed class AdfPipelineRuntimeInfoRow
{
    public string FolderName { get; init; } = "";

    public string PipelineName { get; init; } = "";

    public string ObjectName { get; init; } = "";

    public string ObjectCategory { get; init; } = "";

    public string ObjectType { get; init; } = "";

    public string Status { get; init; } = "";

    public string Description { get; init; } = "";

    public string LatestPipelineRunStatus { get; init; } = "";

    public string LatestPipelineRunId { get; init; } = "";

    public DateTime? LatestPipelineRunStartUtc { get; init; }

    public DateTime? LatestPipelineRunEndUtc { get; init; }

    public double? ExactTimeTakenSeconds { get; init; }

    public string ExactTimeTaken { get; init; } = "";
}
