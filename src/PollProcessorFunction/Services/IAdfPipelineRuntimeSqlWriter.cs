using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IAdfPipelineRuntimeSqlWriter
{
    Task<AdfPipelineRuntimeSqlSaveResult> SaveAsync(
        IReadOnlyList<AdfPipelineRuntimeInfoRow> rows,
        CancellationToken cancellationToken = default);
}
