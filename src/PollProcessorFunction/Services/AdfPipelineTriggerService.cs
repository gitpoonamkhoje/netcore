using System.Text;
using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class AdfPipelineTriggerService : IAdfPipelineTriggerService
{
    private const string ArmScope = "https://management.azure.com/.default";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly ILogger<AdfPipelineTriggerService> _log;

    public AdfPipelineTriggerService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AdfPipelineTriggerService> log)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _credential = new DefaultAzureCredential();
        _log = log;
    }

    public async Task TriggerAsync(PollItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.JobName))
        {
            throw new InvalidOperationException($"Poll item {item.Id} has no job/pipeline name.");
        }

        var triggerUrl = GetSetting("ADFTriggerUrl")
            ?? GetSetting("AdfTriggerUrl");

        if (string.IsNullOrWhiteSpace(triggerUrl))
        {
            throw new InvalidOperationException(
                "ADFTriggerUrl is not configured. Set ADFTriggerUrl or AppResources__AdfTriggerUrl.");
        }

        var triggerUri = BuildTriggerUri(triggerUrl, item.JobName);
        var isArmCreateRun = string.Equals(triggerUri.Host, "management.azure.com", StringComparison.OrdinalIgnoreCase);
        object payload = isArmCreateRun
            ? new { file = item.FileName }
            : new
            {
                pipelineName = item.JobName,
                parameters = new { file = item.FileName }
            };

        var client = _httpClientFactory.CreateClient(nameof(AdfPipelineTriggerService));
        using var request = new HttpRequestMessage(HttpMethod.Post, triggerUri)
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json")
        };

        if (isArmCreateRun)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { ArmScope }),
                cancellationToken);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

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

    private Uri BuildTriggerUri(string triggerUrl, string pipelineName)
    {
        var subscriptionId = GetSetting("DataFactorySubscriptionId") ?? "";
        var resourceGroupName = GetSetting("DataFactoryResourceGroupName") ?? "";
        var factoryName = GetSetting("DataFactoryName") ?? "";
        var apiVersion = GetSetting("AdfManagementApiVersion") ?? "2018-06-01";

        var url = triggerUrl
            .Replace("{sub}", Uri.EscapeDataString(subscriptionId), StringComparison.OrdinalIgnoreCase)
            .Replace("{subscriptionId}", Uri.EscapeDataString(subscriptionId), StringComparison.OrdinalIgnoreCase)
            .Replace("{rg}", Uri.EscapeDataString(resourceGroupName), StringComparison.OrdinalIgnoreCase)
            .Replace("{resourceGroupName}", Uri.EscapeDataString(resourceGroupName), StringComparison.OrdinalIgnoreCase)
            .Replace("{factory}", Uri.EscapeDataString(factoryName), StringComparison.OrdinalIgnoreCase)
            .Replace("{factoryName}", Uri.EscapeDataString(factoryName), StringComparison.OrdinalIgnoreCase)
            .Replace("{pipelineName}", Uri.EscapeDataString(pipelineName), StringComparison.OrdinalIgnoreCase)
            .Replace("{apiVersion}", Uri.EscapeDataString(apiVersion), StringComparison.OrdinalIgnoreCase);

        if (url.Contains('{', StringComparison.Ordinal) || url.Contains('}', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ADFTriggerUrl contains unresolved placeholders. Configure subscription, resource group, factory, and pipeline placeholders correctly.");
        }

        return new Uri(url);
    }

    private string? GetSetting(string name)
    {
        var value = _configuration[name]
            ?? _configuration[$"AppResources:{name}"];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
