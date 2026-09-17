using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TypeSafeAI.Json;

namespace TypeSafeAI;

/// <summary>
/// A piece of content accepted by the TypeSafe API: plain text, a JSON object, or a JSON array.
/// Used for request state, question instructions, and criteria descriptions.
/// </summary>
[JsonConverter(typeof(TypeSafeContentJsonConverter))]
public readonly struct TypeSafeContent : IEquatable<TypeSafeContent>
{
    private TypeSafeContent(string? text, JsonNode? node)
    {
        Text = text;
        Node = node;
    }

    /// <summary>The text value, or <see langword="null"/> when the content is JSON.</summary>
    public string? Text { get; }

    /// <summary>The JSON value, or <see langword="null"/> when the content is text.</summary>
    public JsonNode? Node { get; }

    /// <summary>True when the content is plain text.</summary>
    public bool IsText => Text is not null;

    /// <summary>True when the content is JSON.</summary>
    public bool IsJson => Node is not null;

    /// <summary>True when the content carries neither text nor JSON.</summary>
    public bool IsEmpty => Text is null && Node is null;

    /// <summary>Creates text content.</summary>
    public static TypeSafeContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TypeSafeContent(text, null);
    }

    /// <summary>
    /// Creates content from a node. A JSON string becomes text; anything else is used as-is and must not be attached to another parent.
    /// </summary>
    public static TypeSafeContent FromNode(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return FromParsed(node);
    }

    /// <summary>Creates content from an element by cloning it into a node.</summary>
    public static TypeSafeContent FromElement(JsonElement element) =>
        FromParsed(JsonSerializer.SerializeToNode(element, TypeSafeJsonContext.Default.JsonElement));

    /// <summary>Creates content by parsing raw JSON text.</summary>
    public static TypeSafeContent FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return FromParsed(JsonNode.Parse(json));
    }

    /// <summary>Creates a JSON array of strings, the shape the API expects for sequences of messages or records.</summary>
    public static TypeSafeContent FromStrings(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add((JsonNode?)JsonValue.Create(item));
        }

        return new TypeSafeContent(null, array);
    }

    /// <summary>Serializes a value to JSON content using source-generated metadata. Safe for trimming and AOT.</summary>
    public static TypeSafeContent FromObject<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return FromParsed(JsonSerializer.SerializeToNode(value, typeInfo));
    }

    /// <summary>Serializes a value to JSON content using reflection-based serialization.</summary>
    [RequiresUnreferencedCode("Reflection-based serialization may require types that are trimmed. Use the JsonTypeInfo overload instead.")]
    [RequiresDynamicCode("Reflection-based serialization may require runtime code generation. Use the JsonTypeInfo overload instead.")]
    public static TypeSafeContent FromObject<T>(T value, JsonSerializerOptions? options = null)
    {
        return FromParsed(JsonSerializer.SerializeToNode(value, options ?? TypeSafeJsonDefaults.ContentSerializerOptions));
    }

    // Every JSON entry point lands here so a string value is always text and equality matches the wire form.
    private static TypeSafeContent FromParsed(JsonNode? node) => node switch
    {
        null => default,
        JsonValue value when value.TryGetValue<string>(out var text) => new TypeSafeContent(text, null),
        _ => new TypeSafeContent(null, node),
    };

    /// <summary>Implicitly converts text to content.</summary>
    public static implicit operator TypeSafeContent(string text) => FromText(text);

    /// <summary>Implicitly converts a node to content.</summary>
    public static implicit operator TypeSafeContent(JsonNode node) => FromNode(node);

    /// <summary>Implicitly converts an element to content.</summary>
    public static implicit operator TypeSafeContent(JsonElement element) => FromElement(element);

    /// <summary>Returns the text, or the JSON as a compact string.</summary>
    public override string ToString() => Text ?? Node?.ToJsonString() ?? string.Empty;

    /// <inheritdoc />
    public bool Equals(TypeSafeContent other) =>
        Text is not null
            ? string.Equals(Text, other.Text, StringComparison.Ordinal)
            : other.Text is null && JsonNode.DeepEquals(Node, other.Node);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TypeSafeContent other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Text?.GetHashCode(StringComparison.Ordinal) ?? Node?.ToJsonString().GetHashCode(StringComparison.Ordinal) ?? 0;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(TypeSafeContent left, TypeSafeContent right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(TypeSafeContent left, TypeSafeContent right) => !left.Equals(right);
}
