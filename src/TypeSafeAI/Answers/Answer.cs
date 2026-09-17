using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>
/// An answer to a question. One of <see cref="NoulAnswer"/>, <see cref="ChoiceAnswer"/>, or <see cref="ScoreAnswer"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
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
    public NoulAnswer AsNoul() => this as NoulAnswer ?? throw WrongType("noul");

    /// <summary>Casts to a choice answer.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of a different type.</exception>
    public ChoiceAnswer AsChoice() => this as ChoiceAnswer ?? throw WrongType("choice");

    /// <summary>Casts to a score answer.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of a different type.</exception>
    public ScoreAnswer AsScore() => this as ScoreAnswer ?? throw WrongType("score");

    private TypeSafeResponseValidationException WrongType(string expected) =>
        new($"Expected a {expected} answer but the API returned a {Type} answer.");
}
