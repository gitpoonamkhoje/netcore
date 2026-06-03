using Newtonsoft.Json.Linq;

namespace PollProcessorFunction.Models;

public sealed class AdfPipelineDetails
{
    public string PipelineName { get; init; } = "";

    public JToken Pipeline { get; init; } = new JObject();

    public IReadOnlyList<AdfActivitySummary> Activities { get; init; } = Array.Empty<AdfActivitySummary>();

    public IReadOnlyList<AdfActivitySummary> ScriptActivities { get; init; } = Array.Empty<AdfActivitySummary>();

    public IReadOnlyList<AdfResourceReference> ReferencedResources { get; init; } = Array.Empty<AdfResourceReference>();

    public IReadOnlyList<JToken> Triggers { get; init; } = Array.Empty<JToken>();
}

public sealed class AdfActivitySummary
{
    public string Name { get; init; } = "";

    public string Type { get; init; } = "";

    public JToken? DependsOn { get; init; }

    public JToken? Inputs { get; init; }

    public JToken? Outputs { get; init; }

    public JToken? TypeProperties { get; init; }

    public JToken? Scripts { get; init; }
}

public sealed class AdfResourceReference
{
    public string ReferenceName { get; init; } = "";

    public string ReferenceType { get; init; } = "";

    public string Path { get; init; } = "";
}
