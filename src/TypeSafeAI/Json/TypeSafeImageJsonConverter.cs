using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TypeSafeAI.Json;

/// <summary>Writes a <see cref="TypeSafeImage"/> as its reference string. Images are not read back from JSON.</summary>
public sealed class TypeSafeImageJsonConverter : JsonConverter<TypeSafeImage>
{
    // While TypeSafeContent.FromObject serializes a value, the images written so the content can carry them.
    [ThreadStatic]
    private static bool t_collecting;

    [ThreadStatic]
    private static List<TypeSafeImage>? t_collected;

    /// <inheritdoc />
    public override TypeSafeImage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException($"{nameof(TypeSafeImage)} cannot be read from JSON.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TypeSafeImage value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Reference);
        if (t_collecting)
        {
            (t_collected ??= []).Add(value);
        }
    }

    internal static JsonNode? SerializeCollecting<TValue, TMetadata>(
        TValue value,
        TMetadata metadata,
        Func<TValue, TMetadata, JsonNode?> serialize,
        out TypeSafeImage[]? images)
    {
        var (previousCollecting, previousCollected) = (t_collecting, t_collected);
        (t_collecting, t_collected) = (true, null);
        try
        {
            var node = serialize(value, metadata);
            images = t_collected?.ToArray();
            return node;
        }
        finally
        {
            (t_collecting, t_collected) = (previousCollecting, previousCollected);
        }
    }
}
