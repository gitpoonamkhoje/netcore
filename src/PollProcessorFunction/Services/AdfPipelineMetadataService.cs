using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PollProcessorFunction.Models;

namespace PollProcessorFunction.Services;

public sealed class AdfPipelineMetadataService : IAdfPipelineMetadataService
{
    private const string ArmScope = "https://management.azure.com/.default";
    private const string DefaultApiVersion = "2018-06-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;

    public AdfPipelineMetadataService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _credential = new DefaultAzureCredential();
    }

    public async Task<IReadOnlyList<AdfPipelineDetails>> GetAllPipelineDetailsAsync(CancellationToken cancellationToken = default)
    {
        var pipelines = await ListPipelinesAsync(cancellationToken);
        var triggers = await ListTriggersAsync(cancellationToken);

        return pipelines
            .Select(pipeline => BuildDetails(pipeline, triggers))
            .OrderBy(details => details.PipelineName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AdfPipelineDetails?> GetPipelineDetailsAsync(string pipelineName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
        {
            throw new ArgumentException("Pipeline name is required.", nameof(pipelineName));
        }

        var pipeline = await GetPipelineAsync(pipelineName, cancellationToken);
        if (pipeline is null)
        {
            return null;
        }

        var triggers = await ListTriggersAsync(cancellationToken);
        return BuildDetails(pipeline, triggers);
    }

    public async Task<AdfPipelineRunSummary?> GetLatestPipelineRunAsync(
        string pipelineName,
        TimeSpan lookback,
        CancellationToken cancellationToken = default)
    {
        if (lookback <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), "Lookback must be greater than zero.");
        }

        var runs = await GetPipelineRunsAsync(
            pipelineName,
            DateTime.UtcNow.Subtract(lookback),
            DateTime.UtcNow,
            cancellationToken);

        return runs
            .OrderByDescending(run => run.RunStartUtc ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<AdfPipelineRunSummary>> GetPipelineRunsAsync(
        string pipelineName,
        DateTime lastUpdatedAfterUtc,
        DateTime lastUpdatedBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineName))
        {
            throw new ArgumentException("Pipeline name is required.", nameof(pipelineName));
        }

        if (lastUpdatedAfterUtc >= lastUpdatedBeforeUtc)
        {
            throw new ArgumentException("Last updated start time must be before end time.");
        }

        var payload = new
        {
            lastUpdatedAfter = EnsureUtc(lastUpdatedAfterUtc),
            lastUpdatedBefore = EnsureUtc(lastUpdatedBeforeUtc),
            filters = new[]
            {
                new
                {
                    operand = "PipelineName",
                    @operator = "Equals",
                    values = new[] { pipelineName }
                }
            }
        };

        var json = await PostArmJsonAsync($"{FactoryPath}/queryPipelineRuns", payload, cancellationToken);
        return json["value"] is JArray values
            ? values.OfType<JObject>().Select(ToPipelineRunSummary).ToArray()
            : Array.Empty<AdfPipelineRunSummary>();
    }

    private async Task<IReadOnlyList<JObject>> ListPipelinesAsync(CancellationToken cancellationToken)
    {
        return await GetArmPagedValuesAsync($"{FactoryPath}/pipelines", cancellationToken);
    }

    private async Task<JObject?> GetPipelineAsync(string pipelineName, CancellationToken cancellationToken)
    {
        try
        {
            return await GetArmJsonAsync($"{FactoryPath}/pipelines/{Escape(pipelineName)}", cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<JObject>> ListTriggersAsync(CancellationToken cancellationToken)
    {
        return await GetArmPagedValuesAsync($"{FactoryPath}/triggers", cancellationToken);
    }

    private async Task<IReadOnlyList<JObject>> GetArmPagedValuesAsync(
        string resourcePath,
        CancellationToken cancellationToken)
    {
        var results = new List<JObject>();
        string? nextUrl = null;

        do
        {
            var json = nextUrl is null
                ? await GetArmJsonAsync(resourcePath, cancellationToken)
                : await GetArmJsonByUrlAsync(nextUrl, cancellationToken);

            if (json["value"] is JArray values)
            {
                results.AddRange(values.OfType<JObject>());
            }

            nextUrl = json.Value<string>("nextLink");
        }
        while (!string.IsNullOrWhiteSpace(nextUrl));

        return results;
    }

    private async Task<JObject> GetArmJsonAsync(string resourcePath, CancellationToken cancellationToken)
    {
        var apiVersion = GetSetting("AdfManagementApiVersion")
            ?? DefaultApiVersion;

        return await GetArmJsonByUrlAsync(
            $"https://management.azure.com{resourcePath}?api-version={Uri.EscapeDataString(apiVersion)}",
            cancellationToken);
    }

    private async Task<JObject> GetArmJsonByUrlAsync(string requestUrl, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { ArmScope }),
            cancellationToken);

        var client = _httpClientFactory.CreateClient(nameof(AdfPipelineMetadataService));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUrl);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ADF metadata request failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        return JObject.Parse(body);
    }

    private async Task<JObject> PostArmJsonAsync(
        string resourcePath,
        object payload,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { ArmScope }),
            cancellationToken);

        var apiVersion = GetSetting("AdfManagementApiVersion")
            ?? DefaultApiVersion;

        var client = _httpClientFactory.CreateClient(nameof(AdfPipelineMetadataService));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://management.azure.com{resourcePath}?api-version={Uri.EscapeDataString(apiVersion)}")
        {
            Content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ADF metadata request failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        return JObject.Parse(body);
    }

    private AdfPipelineDetails BuildDetails(JObject pipeline, IReadOnlyList<JObject> allTriggers)
    {
        var pipelineName = pipeline.Value<string>("name") ?? "";
        var activities = new List<JObject>();
        CollectActivityObjects(pipeline["properties"]?["activities"], activities);

        var activitySummaries = activities
            .Select(ToActivitySummary)
            .ToArray();

        return new AdfPipelineDetails
        {
            PipelineName = pipelineName,
            Pipeline = pipeline,
            Activities = activitySummaries,
            ScriptActivities = activitySummaries
                .Where(activity =>
                    string.Equals(activity.Type, "Script", StringComparison.OrdinalIgnoreCase)
                    || activity.Scripts is not null)
                .ToArray(),
            ReferencedResources = CollectResourceReferences(pipeline),
            Triggers = allTriggers
                .Where(trigger => TriggerReferencesPipeline(trigger, pipelineName))
                .ToArray()
        };
    }

    private static void CollectActivityObjects(JToken? token, List<JObject> activities)
    {
        switch (token)
        {
            case JObject obj:
                if (obj["name"] is not null && obj["type"] is not null)
                {
                    activities.Add(obj);
                }

                foreach (var property in obj.Properties())
                {
                    CollectActivityObjects(property.Value, activities);
                }
                break;

            case JArray array:
                foreach (var item in array)
                {
                    CollectActivityObjects(item, activities);
                }
                break;
        }
    }

    private static AdfActivitySummary ToActivitySummary(JObject activity)
    {
        return new AdfActivitySummary
        {
            Name = activity.Value<string>("name") ?? "",
            Type = activity.Value<string>("type") ?? "",
            Status = GetActivityStatus(activity),
            Description = activity.Value<string>("description") ?? "",
            DependsOn = activity["dependsOn"],
            Inputs = activity["inputs"],
            Outputs = activity["outputs"],
            TypeProperties = activity["typeProperties"],
            Scripts = activity.SelectToken("typeProperties.scripts")
        };
    }

    private static string GetActivityStatus(JObject activity)
    {
        var state = activity.Value<string>("state");
        return string.Equals(state, "Inactive", StringComparison.OrdinalIgnoreCase)
            ? "Inactive"
            : "Active";
    }

    private static AdfPipelineRunSummary ToPipelineRunSummary(JObject run)
    {
        var runStart = run.Value<DateTime?>("runStart");
        var runEnd = run.Value<DateTime?>("runEnd");

        return new AdfPipelineRunSummary
        {
            PipelineName = run.Value<string>("pipelineName") ?? "",
            RunId = run.Value<string>("runId") ?? "",
            Status = run.Value<string>("status") ?? "",
            RunStartUtc = runStart,
            RunEndUtc = runEnd,
            DurationSeconds = runStart is not null && runEnd is not null
                ? (runEnd.Value - runStart.Value).TotalSeconds
                : null,
            RawRun = run
        };
    }

    private static bool TriggerReferencesPipeline(JObject trigger, string pipelineName)
    {
        return trigger
            .SelectTokens("$..pipelineReference.referenceName")
            .Values<string>()
            .Any(reference => string.Equals(reference, pipelineName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<AdfResourceReference> CollectResourceReferences(JObject pipeline)
    {
        return pipeline
            .SelectTokens("$..referenceName")
            .Select(token => new AdfResourceReference
            {
                ReferenceName = token.Value<string>() ?? "",
                ReferenceType = (token.Parent?.Parent as JObject)?.Value<string>("type") ?? "",
                Path = token.Path
            })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceName))
            .GroupBy(
                reference => $"{reference.ReferenceType}|{reference.ReferenceName}|{reference.Path}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reference => reference.ReferenceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.ReferenceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string FactoryPath =>
        $"/subscriptions/{Escape(RequiredSetting("DataFactorySubscriptionId"))}" +
        $"/resourceGroups/{Escape(RequiredSetting("DataFactoryResourceGroupName"))}" +
        $"/providers/Microsoft.DataFactory/factories/{Escape(RequiredSetting("DataFactoryName"))}";

    private string RequiredSetting(string name)
    {
        return GetSetting(name)
            ?? throw new InvalidOperationException(
                $"{name} is not configured. Set {name} or AppResources__{name}.");
    }

    private string? GetSetting(string name)
    {
        var value = _configuration[name]
            ?? _configuration[$"AppResources:{name}"];

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
    }
}
