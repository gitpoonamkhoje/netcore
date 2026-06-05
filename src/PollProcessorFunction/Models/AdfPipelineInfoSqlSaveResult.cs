namespace PollProcessorFunction.Models;

public sealed class AdfPipelineInfoSqlSaveResult
{
    public Guid SnapshotId { get; init; }

    public DateTime CapturedAtUtc { get; init; }

    public int PipelineCount { get; init; }

    public int ActivityCount { get; init; }

    public int ScriptActivityCount { get; init; }

    public int ReferenceCount { get; init; }

    public int TriggerCount { get; init; }

    public int TotalRowsInserted { get; init; }
}
