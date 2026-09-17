using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>The answer to a <see cref="ScoreQuestion"/>: the expected level and the distribution over levels.</summary>
public sealed class ScoreAnswer : Answer
{
    /// <summary>The wire name of this answer type.</summary>
    public const string TypeName = "score";

    private ScoreLevel[]? _levels;
    private int? _mostLikelyLevel;

    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => TypeName;

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
    public int MostLikelyLevel => _mostLikelyLevel ??= FindMostLikelyLevel();

    /// <summary>The level index closest to <see cref="Score"/>.</summary>
    [JsonIgnore]
    public int NearestLevel => (int)Math.Round(Score, MidpointRounding.AwayFromZero);

    /// <summary>The levels in index order with their description and probability.</summary>
    [JsonIgnore]
    public IReadOnlyList<ScoreLevel> Levels => _levels ??= BuildLevels();

    /// <summary>Gets the probability of a level index, or 0 when the level does not exist.</summary>
    public double ProbabilityOf(int level) => Probabilities.GetValueOrDefault(level);

    private int FindMostLikelyLevel()
    {
        if (Probabilities.Count == 0)
        {
            return NearestLevel;
        }

        var best = int.MaxValue;
        var bestProbability = double.NegativeInfinity;
        foreach (var (index, probability) in Probabilities)
        {
            if (probability > bestProbability || (probability == bestProbability && index < best))
            {
                best = index;
                bestProbability = probability;
            }
        }

        return best;
    }

    private ScoreLevel[] BuildLevels()
    {
        var indices = new SortedSet<int>(Legend.Keys);
        indices.UnionWith(Probabilities.Keys);

        var levels = new ScoreLevel[indices.Count];
        var i = 0;
        foreach (var index in indices)
        {
            levels[i++] = new ScoreLevel(index, Legend.GetValueOrDefault(index), Probabilities.GetValueOrDefault(index));
        }

        return levels;
    }
}

/// <summary>One level of a score scale with its probability.</summary>
/// <param name="Index">The level index, 0 being the lowest.</param>
/// <param name="Description">The level description as sent in the question.</param>
/// <param name="Probability">The probability the state sits at this level.</param>
public readonly record struct ScoreLevel(int Index, TypeSafeContent? Description, double Probability);
