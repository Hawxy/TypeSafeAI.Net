using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>
/// The answer to a <see cref="BoxQuestion"/>: a box around the target. There is no "not found" answer,
/// so ask a <see cref="NoulQuestion"/> in the same request when the target may be absent.
/// </summary>
public sealed class BoxAnswer : Answer
{
    /// <summary>The wire name of this answer type.</summary>
    public const string TypeName = "box";

    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => TypeName;

    /// <summary>The box's edges as fractions of the image's size.</summary>
    [JsonPropertyName("box")]
    public required BoxCoordinates Box { get; init; }

    /// <summary>Confidence in the box, from 0 to 1, or <see langword="null"/> when the model does not provide one.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

/// <summary>
/// Box edges as fractions from 0 to 1, measured from the image's top-left corner:
/// <see cref="Top"/> and <see cref="Bottom"/> of its height, <see cref="Left"/> and <see cref="Right"/> of its width.
/// </summary>
public sealed class BoxCoordinates
{
    /// <summary>The top edge, as a fraction of the image height from the top.</summary>
    [JsonPropertyName("top")]
    public required double Top { get; init; }

    /// <summary>The bottom edge, as a fraction of the image height from the top.</summary>
    [JsonPropertyName("bottom")]
    public required double Bottom { get; init; }

    /// <summary>The left edge, as a fraction of the image width from the left.</summary>
    [JsonPropertyName("left")]
    public required double Left { get; init; }

    /// <summary>The right edge, as a fraction of the image width from the left.</summary>
    [JsonPropertyName("right")]
    public required double Right { get; init; }

    /// <summary>The box width as a fraction of the image width.</summary>
    [JsonIgnore]
    public double Width => Right - Left;

    /// <summary>The box height as a fraction of the image height.</summary>
    [JsonIgnore]
    public double Height => Bottom - Top;

    /// <summary>Converts the edges to pixel positions in an image of the given size, for drawing or cropping.</summary>
    public (int Left, int Top, int Right, int Bottom) ToPixels(int imageWidth, int imageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
        return (Scale(Left, imageWidth), Scale(Top, imageHeight), Scale(Right, imageWidth), Scale(Bottom, imageHeight));
    }

    private static int Scale(double fraction, int size) => (int)Math.Round(Math.Clamp(fraction, 0, 1) * size, MidpointRounding.AwayFromZero);
}
