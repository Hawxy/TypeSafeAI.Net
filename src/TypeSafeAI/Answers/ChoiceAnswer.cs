using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>The answer to a <see cref="ChoiceQuestion"/>: the most likely label and the full distribution.</summary>
public sealed class ChoiceAnswer : Answer
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "choice";

    /// <summary>The label with the highest probability.</summary>
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    /// <summary>Probability per label, summing to about 1.</summary>
    [JsonPropertyName("probabilities")]
    public IReadOnlyDictionary<string, double> Probabilities { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>How concentrated the distribution is, from 0 to 1. This summarises certainty, not correctness.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Gets the probability of a label, or 0 when the label was not offered.</summary>
    public double ProbabilityOf(string label) => Probabilities.TryGetValue(label, out var p) ? p : 0;

    /// <summary>The labels ordered from most to least likely.</summary>
    public IEnumerable<KeyValuePair<string, double>> Ranked() => Probabilities.OrderByDescending(p => p.Value);
}
