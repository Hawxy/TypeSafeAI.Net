using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>A pick-one question. The answer is the most likely label plus a probability per label.</summary>
public sealed class ChoiceQuestion : Question
{
    /// <summary>Creates a pick-one question. Criteria map each label to an optional description and must contain at least one label.</summary>
    [JsonConstructor]
    public ChoiceQuestion(TypeSafeContent? instructions, IReadOnlyDictionary<string, TypeSafeContent?> criteria)
        : base(instructions)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0)
        {
            throw new ArgumentException("A choice question needs at least one label.", nameof(criteria));
        }

        foreach (var label in criteria.Keys)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("Choice labels must be non-empty.", nameof(criteria));
            }
        }

        Criteria = criteria;
    }

    /// <summary>The wire name of this question type.</summary>
    public const string TypeName = "choice";

    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => TypeName;

    /// <summary>The labels to choose from, each with an optional description.</summary>
    [JsonPropertyName("criteria")]
    [JsonPropertyOrder(2)]
    public IReadOnlyDictionary<string, TypeSafeContent?> Criteria { get; }

    /// <summary>The labels in declaration order.</summary>
    [JsonIgnore]
    public IEnumerable<string> Labels => Criteria.Keys;

    internal static Dictionary<string, TypeSafeContent?> CriteriaFromLabels(IEnumerable<string> labels)
    {
        var criteria = new Dictionary<string, TypeSafeContent?>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            if (!criteria.TryAdd(label, null))
            {
                throw new ArgumentException($"Duplicate choice label '{label}'.", nameof(labels));
            }
        }

        return criteria;
    }
}
