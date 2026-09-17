using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TypeSafeAI.Json;

/// <summary>Writes <see cref="TypeSafeContent"/> as a JSON string or as the embedded JSON value.</summary>
public sealed class TypeSafeContentJsonConverter : JsonConverter<TypeSafeContent>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override TypeSafeContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return default;
            case JsonTokenType.String:
                return TypeSafeContent.FromText(reader.GetString()!);
            default:
                var node = JsonNode.Parse(ref reader);
                return node is null ? default : TypeSafeContent.FromNode(node);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TypeSafeContent value, JsonSerializerOptions options)
    {
        if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
        }
        else if (value.Node is not null)
        {
            value.Node.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
