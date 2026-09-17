using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>
/// A judgment to ask about a state. One of <see cref="NoulQuestion"/>, <see cref="ChoiceQuestion"/>, or <see cref="ScoreQuestion"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public abstract class Question
{
    private protected Question(TypeSafeContent? instructions)
    {
        Instructions = instructions;
    }

    /// <summary>The wire name of this question type: <c>noul</c>, <c>choice</c>, or <c>score</c>.</summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>What to evaluate, as text or structured JSON.</summary>
    [JsonPropertyName("instructions")]
    [JsonPropertyOrder(1)]
    public TypeSafeContent? Instructions { get; }

    /// <summary>Creates a yes/no question. The answer is the probability of yes.</summary>
    /// <param name="instructions">The statement or question to judge.</param>
    /// <param name="yes">Optional clarification of what a yes answer means.</param>
    /// <param name="no">Optional clarification of what a no answer means.</param>
    public static NoulQuestion Noul(TypeSafeContent? instructions, TypeSafeContent? yes = null, TypeSafeContent? no = null) =>
        new(instructions, yes is null && no is null ? null : new NoulCriteria(yes, no));

    /// <summary>Creates a pick-one question over labelled options with optional descriptions.</summary>
    public static ChoiceQuestion Choice(TypeSafeContent? instructions, IReadOnlyDictionary<string, TypeSafeContent?> criteria) =>
        new(instructions, criteria);

    /// <summary>Creates a pick-one question over plain labels with no descriptions.</summary>
    public static ChoiceQuestion Choice(TypeSafeContent? instructions, params string[] labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return new ChoiceQuestion(instructions, ChoiceQuestion.CriteriaFromLabels(labels));
    }

    /// <summary>Creates a pick-one question whose labels and descriptions come from an enum, see <see cref="LabelAttribute"/>.</summary>
    public static ChoiceQuestion Choice<TEnum>(TypeSafeContent? instructions) where TEnum : struct, Enum =>
        new(instructions, EnumLabels<TEnum>.ToChoiceCriteria());

    /// <summary>Creates a rating question over ordered levels. Index 0 is the lowest level; at least two are required.</summary>
    public static ScoreQuestion Score(TypeSafeContent? instructions, params TypeSafeContent?[] levels) =>
        new(instructions, levels);

    /// <summary>Creates a rating question over ordered text levels. Index 0 is the lowest level; at least two are required.</summary>
    public static ScoreQuestion Score(TypeSafeContent? instructions, IEnumerable<string> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return new ScoreQuestion(instructions, levels.Select(l => (TypeSafeContent?)TypeSafeContent.FromText(l)).ToArray());
    }

    /// <summary>Creates a rating question whose ordered levels come from an enum's members, see <see cref="LabelAttribute"/>.</summary>
    public static ScoreQuestion Score<TEnum>(TypeSafeContent? instructions) where TEnum : struct, Enum =>
        new(instructions, EnumLabels<TEnum>.ToScoreCriteria());
}
