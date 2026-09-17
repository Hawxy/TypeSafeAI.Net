using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>A yes/no question. The answer is the probability that the answer is yes.</summary>
public sealed class NoulQuestion : Question
{
    /// <summary>Creates a yes/no question.</summary>
    [JsonConstructor]
    public NoulQuestion(TypeSafeContent? instructions, NoulCriteria? criteria = null)
        : base(instructions)
    {
        Criteria = criteria;
    }

    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "noul";

    /// <summary>Optional clarification of what yes and no mean.</summary>
    [JsonPropertyName("criteria")]
    [JsonPropertyOrder(2)]
    public NoulCriteria? Criteria { get; }
}

/// <summary>Clarifies the boundary between a yes and a no answer.</summary>
public sealed class NoulCriteria
{
    /// <summary>Creates noul criteria.</summary>
    [JsonConstructor]
    public NoulCriteria(TypeSafeContent? yes = null, TypeSafeContent? no = null)
    {
        Yes = yes;
        No = no;
    }

    /// <summary>What a yes answer means.</summary>
    [JsonPropertyName("true")]
    public TypeSafeContent? Yes { get; }

    /// <summary>What a no answer means.</summary>
    [JsonPropertyName("false")]
    public TypeSafeContent? No { get; }
}
