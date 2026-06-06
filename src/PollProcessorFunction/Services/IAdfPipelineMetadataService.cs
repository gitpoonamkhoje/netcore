using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IAdfPipelineMetadataService
{
    Task<IReadOnlyList<AdfPipelineDetails>> GetAllPipelineDetailsAsync(CancellationToken cancellationToken = default);

    Task<AdfPipelineDetails?> GetPipelineDetailsAsync(string pipelineName, CancellationToken cancellationToken = default);

    Task<AdfPipelineRunSummary?> GetLatestPipelineRunAsync(
        string pipelineName,
        TimeSpan lookback,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdfPipelineRunSummary>> GetPipelineRunsAsync(
        string pipelineName,
        DateTime lastUpdatedAfterUtc,
        DateTime lastUpdatedBeforeUtc,
        CancellationToken cancellationToken = default);
}
