using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>A rating question over ordered levels. The answer is a probability-weighted position on the scale.</summary>
public sealed class ScoreQuestion : Question
{
    /// <summary>Creates a rating question. Levels are ordered from lowest (index 0) to highest and at least two are required.</summary>
    [JsonConstructor]
    public ScoreQuestion(TypeSafeContent? instructions, IReadOnlyList<TypeSafeContent?> criteria)
        : base(instructions)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count < 2)
        {
            throw new ArgumentException("A score question needs at least two levels.", nameof(criteria));
        }

        Criteria = criteria;
    }

    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "score";

    /// <summary>The level descriptions, lowest first.</summary>
    [JsonPropertyName("criteria")]
    [JsonPropertyOrder(2)]
    public IReadOnlyList<TypeSafeContent?> Criteria { get; }

    /// <summary>The number of levels on the scale.</summary>
    [JsonIgnore]
    public int LevelCount => Criteria.Count;
}
