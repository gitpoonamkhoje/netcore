namespace PollProcessorFunction.Models;

public sealed record SqlConditionEvaluation(
    bool IsMet,
    string? ActualValue = null,
    string? ExpectedValue = null);
