using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>The answer to a <see cref="NoulQuestion"/>: the probability that the answer is yes.</summary>
public sealed class NoulAnswer : Answer
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "noul";

    /// <summary>Probability of yes, from 0 to 1. A value near 0.5 means the model finds yes and no similarly likely.</summary>
    [JsonPropertyName("noul")]
    public required double Probability { get; init; }

    /// <summary>True when <see cref="Probability"/> is at or above the threshold.</summary>
    public bool IsYes(double threshold = 0.5) => Probability >= threshold;
}
