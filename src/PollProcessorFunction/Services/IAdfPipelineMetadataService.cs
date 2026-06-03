using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IAdfPipelineMetadataService
{
    Task<IReadOnlyList<AdfPipelineDetails>> GetAllPipelineDetailsAsync(CancellationToken cancellationToken = default);

    Task<AdfPipelineDetails?> GetPipelineDetailsAsync(string pipelineName, CancellationToken cancellationToken = default);
}
