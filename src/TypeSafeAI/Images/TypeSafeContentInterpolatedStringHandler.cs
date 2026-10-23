using System.Runtime.CompilerServices;

namespace TypeSafeAI;

/// <summary>
/// Builds text content from an interpolated string, writing each <see cref="TypeSafeImage"/> as its reference and keeping the image.
/// Used by <see cref="TypeSafeContent.FromText(ref TypeSafeContentInterpolatedStringHandler)"/>.
/// </summary>
[InterpolatedStringHandler]
public ref struct TypeSafeContentInterpolatedStringHandler
{
    private DefaultInterpolatedStringHandler _text;
    private List<TypeSafeImage>? _assets;

    /// <summary>Creates the handler. Called by the compiler.</summary>
    public TypeSafeContentInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _text = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
    }

    /// <summary>Appends literal text.</summary>
    public void AppendLiteral(string value) => _text.AppendLiteral(value);

    /// <summary>Appends an image's reference and keeps the image.</summary>
    public void AppendFormatted(TypeSafeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _text.AppendLiteral(image.Reference);
        (_assets ??= []).Add(image);
    }

    /// <summary>Appends content as text and keeps its images.</summary>
    public void AppendFormatted(TypeSafeContent content)
    {
        _text.AppendLiteral(content.ToString());
        if (content.Assets.Count > 0)
        {
            (_assets ??= []).AddRange(content.Assets);
        }
    }

    /// <summary>Appends a string.</summary>
    public void AppendFormatted(string? value) => _text.AppendFormatted(value);

    /// <summary>Appends a span of characters.</summary>
    public void AppendFormatted(ReadOnlySpan<char> value) => _text.AppendFormatted(value);

    /// <summary>Appends a value.</summary>
    public void AppendFormatted<T>(T value) => _text.AppendFormatted(value);

    /// <summary>Appends a value with a format string.</summary>
    public void AppendFormatted<T>(T value, string? format) => _text.AppendFormatted(value, format);

    /// <summary>Appends a value with an alignment.</summary>
    public void AppendFormatted<T>(T value, int alignment) => _text.AppendFormatted(value, alignment);

    /// <summary>Appends a value with an alignment and a format string.</summary>
    public void AppendFormatted<T>(T value, int alignment, string? format) => _text.AppendFormatted(value, alignment, format);

    internal TypeSafeContent ToContent() => new(_text.ToStringAndClear(), null, _assets?.ToArray());
}
