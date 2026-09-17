using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>The answer to a <see cref="ScoreQuestion"/>: the expected level and the distribution over levels.</summary>
public sealed class ScoreAnswer : Answer
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "score";

    /// <summary>The probability-weighted level. May fall between the integer levels.</summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    /// <summary>The level descriptions keyed by level index.</summary>
    [JsonPropertyName("legend")]
    public IReadOnlyDictionary<int, TypeSafeContent?> Legend { get; init; } = new Dictionary<int, TypeSafeContent?>();

    /// <summary>Probability per level index, summing to about 1.</summary>
    [JsonPropertyName("probabilities")]
    public IReadOnlyDictionary<int, double> Probabilities { get; init; } = new Dictionary<int, double>();

    /// <summary>How concentrated the distribution is, from 0 to 1. This summarises certainty, not correctness.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>The level index with the highest probability.</summary>
    [JsonIgnore]
    public int MostLikelyLevel => Probabilities.Count == 0
        ? NearestLevel
        : Probabilities.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;

    /// <summary>The level index closest to <see cref="Score"/>.</summary>
    [JsonIgnore]
    public int NearestLevel => (int)Math.Round(Score, MidpointRounding.AwayFromZero);

    /// <summary>The levels in index order with their description and probability.</summary>
    [JsonIgnore]
    public IReadOnlyList<ScoreLevel> Levels
    {
        get
        {
            var indices = Legend.Keys.Union(Probabilities.Keys).OrderBy(i => i);
            return indices
                .Select(i => new ScoreLevel(i, Legend.TryGetValue(i, out var d) ? d : null, Probabilities.TryGetValue(i, out var p) ? p : 0))
                .ToArray();
        }
    }

    /// <summary>Gets the probability of a level index, or 0 when the level does not exist.</summary>
    public double ProbabilityOf(int level) => Probabilities.TryGetValue(level, out var p) ? p : 0;
}

/// <summary>One level of a score scale with its probability.</summary>
/// <param name="Index">The level index, 0 being the lowest.</param>
/// <param name="Description">The level description as sent in the question.</param>
/// <param name="Probability">The probability the state sits at this level.</param>
public readonly record struct ScoreLevel(int Index, TypeSafeContent? Description, double Probability);
