namespace PollProcessorFunction.Models;

public sealed class AdfPipelineRuntimeSqlSaveResult
{
    public Guid SnapshotId { get; init; }

    public DateTime CapturedAtUtc { get; init; }

    public int TotalRowsInserted { get; init; }
}
