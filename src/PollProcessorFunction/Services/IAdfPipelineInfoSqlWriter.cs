using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IAdfPipelineInfoSqlWriter
{
    Task<AdfPipelineInfoSqlSaveResult> SaveAsync(
        IReadOnlyList<AdfPipelineDetails> pipelines,
        CancellationToken cancellationToken = default);
}
