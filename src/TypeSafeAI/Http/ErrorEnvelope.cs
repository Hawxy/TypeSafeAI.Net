using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafeAI;

// Best-effort shape of an error body. The API does not document it, so every field is optional.
internal sealed class ErrorEnvelope
{
    [JsonPropertyName("error")]
    public JsonElement? Error { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("detail")]
    public JsonElement? Detail { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    public (string? Type, string? Message) Describe()
    {
        var type = Type;
        var message = Message;

        if (Error is { } error)
        {
            if (error.ValueKind == JsonValueKind.String)
            {
                message ??= error.GetString();
            }
            else if (error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    type ??= t.GetString();
                }

                if (error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    message ??= m.GetString();
                }
            }
        }

        if (message is null && Detail is { } detail)
        {
            message = detail.ValueKind == JsonValueKind.String ? detail.GetString() : detail.GetRawText();
        }

        return (type, message);
    }
}
