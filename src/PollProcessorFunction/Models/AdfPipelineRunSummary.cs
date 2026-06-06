using Newtonsoft.Json.Linq;

namespace PollProcessorFunction.Models;

public sealed class AdfPipelineRunSummary
{
    public string PipelineName { get; init; } = "";

    public string RunId { get; init; } = "";

    public string Status { get; init; } = "";

    public DateTime? RunStartUtc { get; init; }

    public DateTime? RunEndUtc { get; init; }

    public double? DurationSeconds { get; init; }

    public JToken RawRun { get; init; } = new JObject();
}
