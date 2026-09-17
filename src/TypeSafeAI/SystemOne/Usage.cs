using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>Token usage reported for a request.</summary>
public sealed class Usage
{
    /// <summary>Input tokens consumed. Input tokens are what TypeSafe bills.</summary>
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    /// <summary>Output tokens produced.</summary>
    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }
}
