using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using IfrFunction.Services;

namespace IfrFunction.Functions;

public sealed class AptSpectrBlobFunction
{
    private readonly IAptSpectrBlobProcessor _processor;
    private readonly ILogger<AptSpectrBlobFunction> _logger;

    public AptSpectrBlobFunction(IAptSpectrBlobProcessor processor, ILogger<AptSpectrBlobFunction> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    /// <summary>
    /// Fires when a new APT or SPECTR file lands in blob inbound folders.
    /// Paths: {container}/apt/inbound/{name} and {container}/spectr/inbound/{name}
    /// </summary>
    [Function("AptSpectrBlobInbound")]
    public async Task ProcessInboundAsync(
        [BlobTrigger("%AppResources:BlobContainerName%/apt/inbound/{name}", Connection = "AzureWebJobsStorage")]
        Stream stream,
        string name,
        FunctionContext context)
    {
        _logger.LogInformation("APT inbound blob arrived: {Name}", name);
        await _processor.ProcessBlobAsync($"apt/inbound/{name}", context.CancellationToken);
    }

    [Function("AptSpectrSpectrBlobInbound")]
    public async Task ProcessSpectrInboundAsync(
        [BlobTrigger("%AppResources:BlobContainerName%/spectr/inbound/{name}", Connection = "AzureWebJobsStorage")]
        Stream stream,
        string name,
        FunctionContext context)
    {
        _logger.LogInformation("SPECTR inbound blob arrived: {Name}", name);
        await _processor.ProcessBlobAsync($"spectr/inbound/{name}", context.CancellationToken);
    }

    /// <summary>
    /// Fires when a failed file is moved to the retry folder for another attempt.
    /// </summary>
    [Function("AptSpectrBlobRetry")]
    public async Task ProcessAptRetryAsync(
        [BlobTrigger("%AppResources:BlobContainerName%/apt/retry/{name}", Connection = "AzureWebJobsStorage")]
        Stream stream,
        string name,
        FunctionContext context)
    {
        _logger.LogInformation("APT retry blob: {Name}", name);
        await _processor.ProcessBlobAsync($"apt/retry/{name}", context.CancellationToken);
    }

    [Function("AptSpectrSpectrBlobRetry")]
    public async Task ProcessSpectrRetryAsync(
        [BlobTrigger("%AppResources:BlobContainerName%/spectr/retry/{name}", Connection = "AzureWebJobsStorage")]
        Stream stream,
        string name,
        FunctionContext context)
    {
        _logger.LogInformation("SPECTR retry blob: {Name}", name);
        await _processor.ProcessBlobAsync($"spectr/retry/{name}", context.CancellationToken);
    }
}
