using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public interface IAdfPipelineTriggerService
{
    Task TriggerAsync(PollItem item, CancellationToken cancellationToken = default);
}
