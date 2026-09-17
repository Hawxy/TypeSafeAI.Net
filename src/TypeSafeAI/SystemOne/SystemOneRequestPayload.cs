using System.Text.Json.Serialization;

namespace TypeSafeAI;

// The exact wire shape of POST /v1/systemone, with the model already resolved.
internal sealed class SystemOneRequestPayload
{
    [JsonPropertyName("state")]
    [JsonPropertyOrder(0)]
    public required TypeSafeContent State { get; init; }

    [JsonPropertyName("model")]
    [JsonPropertyOrder(1)]
    public required string Model { get; init; }

    [JsonPropertyName("questions")]
    [JsonPropertyOrder(2)]
    public required IReadOnlyDictionary<string, Question> Questions { get; init; }
}
