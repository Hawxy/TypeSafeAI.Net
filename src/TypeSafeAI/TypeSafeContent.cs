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
/// Content can carry <see cref="TypeSafeImage"/> assets, which are uploaded with any request that uses the content.
/// </summary>
[JsonConverter(typeof(TypeSafeContentJsonConverter))]
public readonly struct TypeSafeContent : IEquatable<TypeSafeContent>
{
    private readonly TypeSafeImage[]? _assets;

    internal TypeSafeContent(string? text, JsonNode? node, TypeSafeImage[]? assets = null)
    {
        Text = text;
        Node = node;
        _assets = assets;
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

    /// <summary>The images this content refers to. They are not compared by <see cref="Equals(TypeSafeContent)"/>.</summary>
    public IReadOnlyList<TypeSafeImage> Assets => _assets ?? [];

    /// <summary>Creates text content.</summary>
    public static TypeSafeContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TypeSafeContent(text, null);
    }

    /// <summary>
    /// Creates text from an interpolated string. Each <see cref="TypeSafeImage"/> in it is written as its reference and sent with the content.
    /// </summary>
    /// <example>
    /// <code>
    /// var question = Question.Noul(TypeSafeContent.FromText($"Is the dog in `photo` the same dog as in {poster}?"));
    /// </code>
    /// </example>
    public static TypeSafeContent FromText(ref TypeSafeContentInterpolatedStringHandler text) => text.ToContent();

    /// <summary>Creates content that is a single image.</summary>
    public static TypeSafeContent FromImage(TypeSafeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new TypeSafeContent(image.Reference, null, [image]);
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

    /// <summary>
    /// Serializes a value to JSON content using source-generated metadata. Safe for trimming and AOT.
    /// <see cref="TypeSafeImage"/> values in it are written as their references and sent with the content.
    /// </summary>
    public static TypeSafeContent FromObject<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var node = TypeSafeImageJsonConverter.SerializeCollecting(value, typeInfo, static (v, t) => JsonSerializer.SerializeToNode(v, t), out var images);
        return FromParsed(node, images);
    }

    /// <summary>
    /// Serializes a value to JSON content using reflection-based serialization.
    /// <see cref="TypeSafeImage"/> values in it are written as their references and sent with the content.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based serialization may require types that are trimmed. Use the JsonTypeInfo overload instead.")]
    [RequiresDynamicCode("Reflection-based serialization may require runtime code generation. Use the JsonTypeInfo overload instead.")]
    public static TypeSafeContent FromObject<T>(T value, JsonSerializerOptions? options = null)
    {
        var node = TypeSafeImageJsonConverter.SerializeCollecting(
            value,
            options ?? TypeSafeJsonDefaults.ContentSerializerOptions,
            static (v, o) => JsonSerializer.SerializeToNode(v, o),
            out var images);
        return FromParsed(node, images);
    }

    /// <summary>Returns this content with images added, for references written into the content by hand.</summary>
    public TypeSafeContent WithAssets(params IEnumerable<TypeSafeImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var added = images.ToArray();
        if (Array.IndexOf(added, null) >= 0)
        {
            throw new ArgumentException("Images must not be null.", nameof(images));
        }

        return added.Length == 0 ? this : new TypeSafeContent(Text, Node, _assets is null ? added : [.. _assets, .. added]);
    }

    // Every JSON entry point lands here so a string value is always text and equality matches the wire form.
    private static TypeSafeContent FromParsed(JsonNode? node, TypeSafeImage[]? assets = null) => node switch
    {
        null => default,
        JsonValue value when value.TryGetValue<string>(out var text) => new TypeSafeContent(text, null, assets),
        _ => new TypeSafeContent(null, node, assets),
    };

    /// <summary>Implicitly converts text to content.</summary>
    public static implicit operator TypeSafeContent(string text) => FromText(text);

    /// <summary>Implicitly converts a node to content.</summary>
    public static implicit operator TypeSafeContent(JsonNode node) => FromNode(node);

    /// <summary>Implicitly converts an image to content.</summary>
    public static implicit operator TypeSafeContent(TypeSafeImage image) => FromImage(image);

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
