using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class AdfPipelineTriggerService : IAdfPipelineTriggerService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdfPipelineTriggerService> _log;

    public AdfPipelineTriggerService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AdfPipelineTriggerService> log)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _log = log;
    }

    public async Task TriggerAsync(PollItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.JobName))
        {
            throw new InvalidOperationException($"Poll item {item.Id} has no job/pipeline name.");
        }

        var triggerUrl = _configuration["ADFTriggerUrl"]
            ?? _configuration["AppResources:AdfTriggerUrl"];

        if (string.IsNullOrWhiteSpace(triggerUrl))
        {
            throw new InvalidOperationException(
                "ADFTriggerUrl is not configured. Set ADFTriggerUrl or AppResources__AdfTriggerUrl.");
        }

        var payload = new
        {
            pipelineName = item.JobName,
            parameters = new { file = item.FileName }
        };

        var client = _httpClientFactory.CreateClient(nameof(AdfPipelineTriggerService));
        using var request = new HttpRequestMessage(HttpMethod.Post, triggerUrl)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"ADF trigger failed for pipeline '{item.JobName}' ({(int)response.StatusCode}): {body}");
        }

        _log.LogInformation(
            "Triggered ADF pipeline {Pipeline} for poll item {ItemId}, file {FileName}",
            item.JobName,
            item.Id,
            item.FileName);
    }
}
