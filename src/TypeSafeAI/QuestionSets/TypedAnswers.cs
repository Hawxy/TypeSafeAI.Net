namespace TypeSafeAI;

/// <summary>A <see cref="ChoiceAnswer"/> projected onto an enum.</summary>
/// <typeparam name="TEnum">The enum whose members are the labels.</typeparam>
public sealed class ChoiceAnswer<TEnum>
    where TEnum : struct, Enum
{
    internal ChoiceAnswer(TEnum choice, IReadOnlyDictionary<TEnum, double> probabilities, ChoiceAnswer raw)
    {
        Choice = choice;
        Probabilities = probabilities;
        Raw = raw;
    }

    /// <summary>The most likely option.</summary>
    public TEnum Choice { get; }

    /// <summary>The wire label of <see cref="Choice"/>.</summary>
    public string Label => Raw.Choice;

    /// <summary>Probability per option.</summary>
    public IReadOnlyDictionary<TEnum, double> Probabilities { get; }

    /// <summary>How concentrated the distribution is, from 0 to 1.</summary>
    public double Confidence => Raw.Confidence;

    /// <summary>The untyped answer.</summary>
    public ChoiceAnswer Raw { get; }

    /// <summary>Gets the probability of an option, or 0 when it was not returned.</summary>
    public double ProbabilityOf(TEnum option) => Probabilities.GetValueOrDefault(option);

    /// <summary>The options ordered from most to least likely.</summary>
    public IEnumerable<KeyValuePair<TEnum, double>> Ranked() => Probabilities.OrderByDescending(p => p.Value);

    /// <inheritdoc />
    public override string ToString() => $"{Choice} ({Confidence:P0})";
}

/// <summary>A <see cref="ScoreAnswer"/> projected onto an enum whose members are the levels in order.</summary>
/// <typeparam name="TEnum">The enum whose members are the levels.</typeparam>
public sealed class ScoreAnswer<TEnum>
    where TEnum : struct, Enum
{
    internal ScoreAnswer(
        TEnum nearest,
        TEnum mostLikely,
        IReadOnlyDictionary<TEnum, double> probabilities,
        IReadOnlyDictionary<TEnum, TypeSafeContent?> legend,
        ScoreAnswer raw)
    {
        Nearest = nearest;
        MostLikely = mostLikely;
        Probabilities = probabilities;
        Legend = legend;
        Raw = raw;
    }

    /// <summary>The probability-weighted level on the index scale, where 0 is the first enum member.</summary>
    public double Score => Raw.Score;

    /// <summary>The level closest to <see cref="Score"/>.</summary>
    public TEnum Nearest { get; }

    /// <summary>The level with the highest probability.</summary>
    public TEnum MostLikely { get; }

    /// <summary>Probability per level.</summary>
    public IReadOnlyDictionary<TEnum, double> Probabilities { get; }

    /// <summary>Description per level as returned by the API.</summary>
    public IReadOnlyDictionary<TEnum, TypeSafeContent?> Legend { get; }

    /// <summary>How concentrated the distribution is, from 0 to 1.</summary>
    public double Confidence => Raw.Confidence;

    /// <summary>The untyped answer.</summary>
    public ScoreAnswer Raw { get; }

    /// <summary>Gets the probability of a level, or 0 when it was not returned.</summary>
    public double ProbabilityOf(TEnum level) => Probabilities.GetValueOrDefault(level);

    /// <summary>True when <see cref="Score"/> is at or above the given level's index.</summary>
    public bool IsAtLeast(TEnum level) => Score >= EnumLabels<TEnum>.IndexOf(level);

    /// <inheritdoc />
    public override string ToString() => $"{Score:F2} ~ {Nearest} ({Confidence:P0})";
}
