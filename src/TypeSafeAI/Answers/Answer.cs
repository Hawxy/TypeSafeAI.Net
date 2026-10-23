using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>
/// An answer to a question. One of <see cref="NoulAnswer"/>, <see cref="ChoiceAnswer"/>, <see cref="ScoreAnswer"/>, or <see cref="BoxAnswer"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), NoulAnswer.TypeName)]
[JsonDerivedType(typeof(ChoiceAnswer), ChoiceAnswer.TypeName)]
[JsonDerivedType(typeof(ScoreAnswer), ScoreAnswer.TypeName)]
[JsonDerivedType(typeof(BoxAnswer), BoxAnswer.TypeName)]
public abstract class Answer
{
    private protected Answer()
    {
    }

    /// <summary>The wire name of the answer type.</summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>Properties returned by the API that this SDK does not model.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalData { get; set; }

    /// <summary>Casts to a noul answer.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of a different type.</exception>
    public NoulAnswer AsNoul() => this as NoulAnswer ?? throw WrongType(NoulAnswer.TypeName);

    /// <summary>Casts to a choice answer.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of a different type.</exception>
    public ChoiceAnswer AsChoice() => this as ChoiceAnswer ?? throw WrongType(ChoiceAnswer.TypeName);

    /// <summary>Casts to a score answer.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of a different type.</exception>
    public ScoreAnswer AsScore() => this as ScoreAnswer ?? throw WrongType(ScoreAnswer.TypeName);

    /// <summary>Casts to a box answer.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of a different type.</exception>
    public BoxAnswer AsBox() => this as BoxAnswer ?? throw WrongType(BoxAnswer.TypeName);

    private TypeSafeResponseValidationException WrongType(string expected) =>
        new($"Expected a {expected} answer but the API returned a {Type} answer.");
}
