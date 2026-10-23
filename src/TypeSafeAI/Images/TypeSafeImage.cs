using System.Text;
using System.Text.Json.Serialization;
using TypeSafeAI.Json;

namespace TypeSafeAI;

/// <summary>The file format of a <see cref="TypeSafeImage"/>.</summary>
public enum TypeSafeImageFormat
{
    /// <summary>PNG.</summary>
    Png,

    /// <summary>JPEG.</summary>
    Jpeg,
}

/// <summary>
/// A PNG or JPEG image sent with a System One request. Use it anywhere content goes (state, instructions, or criteria):
/// it is written as its <see cref="Reference"/> and the SDK uploads the file with the request.
/// Requests that contain images send the <c>TypeSafe-Danger-Zone-Images: true</c> header.
/// </summary>
/// <remarks>
/// To place an image inside text, use <see cref="TypeSafeContent.FromText(ref TypeSafeContentInterpolatedStringHandler)"/>
/// with an interpolated string. A plain interpolated <see cref="string"/> contains the reference but not the image.
/// </remarks>
[JsonConverter(typeof(TypeSafeImageJsonConverter))]
public sealed class TypeSafeImage
{
    private const int MaxReferenceBytes = 1024;

    private TypeSafeImage(ReadOnlyMemory<byte> data, TypeSafeImageFormat? format, string? reference)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("The image has no data.", nameof(data));
        }

        if (reference is not null)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                throw new ArgumentException("An image reference must not be empty.", nameof(reference));
            }

            if (Encoding.UTF8.GetByteCount(reference) > MaxReferenceBytes)
            {
                throw new ArgumentException($"An image reference must be at most {MaxReferenceBytes} bytes of UTF-8.", nameof(reference));
            }
        }

        Data = data;
        Format = format ?? Detect(data.Span);
        Reference = reference ?? Guid.NewGuid().ToString();
    }

    /// <summary>
    /// The text that stands for the image in the request. Defaults to a new UUID; choose your own only when it cannot occur in your text by accident.
    /// </summary>
    public string Reference { get; }

    /// <summary>The image file bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The format given or detected from the bytes, or <see langword="null"/> when neither applies. The API detects the format itself.</summary>
    public TypeSafeImageFormat? Format { get; }

    // The API's name for the format, also used as the file extension.
    internal string? FormatName => Format switch
    {
        TypeSafeImageFormat.Png => "png",
        TypeSafeImageFormat.Jpeg => "jpg",
        _ => null,
    };

    internal string MediaType => Format switch
    {
        TypeSafeImageFormat.Png => "image/png",
        TypeSafeImageFormat.Jpeg => "image/jpeg",
        _ => "application/octet-stream",
    };

    /// <summary>Creates an image from file bytes.</summary>
    /// <param name="data">The PNG or JPEG file bytes. The memory is not copied.</param>
    /// <param name="format">The format, when known. Detected from the bytes otherwise.</param>
    /// <param name="reference">The reference to send the image under. Defaults to a new UUID.</param>
    public static TypeSafeImage FromBytes(ReadOnlyMemory<byte> data, TypeSafeImageFormat? format = null, string? reference = null) =>
        new(data, format, reference);

    /// <summary>Creates an image from base64-encoded file bytes, without a <c>data:</c> prefix.</summary>
    /// <exception cref="FormatException"><paramref name="base64"/> is not valid base64.</exception>
    public static TypeSafeImage FromBase64(string base64, TypeSafeImageFormat? format = null, string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(base64);
        return new TypeSafeImage(Convert.FromBase64String(base64), format, reference);
    }

    /// <summary>Reads an image file.</summary>
    public static async Task<TypeSafeImage> FromFileAsync(string path, string? reference = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new TypeSafeImage(data, null, reference);
    }

    /// <summary>Reads an image from the current position of a stream to its end.</summary>
    public static async Task<TypeSafeImage> FromStreamAsync(Stream stream, string? reference = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek)
        {
            var data = new byte[stream.Length - stream.Position];
            await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            return new TypeSafeImage(data, null, reference);
        }

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new TypeSafeImage(new ReadOnlyMemory<byte>(buffer.GetBuffer(), 0, (int)buffer.Length), null, reference);
    }

    /// <summary>Returns <see cref="Reference"/>.</summary>
    public override string ToString() => Reference;

    internal bool HasSameData(TypeSafeImage other) => ReferenceEquals(this, other) || Data.Span.SequenceEqual(other.Data.Span);

    private static TypeSafeImageFormat? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return TypeSafeImageFormat.Png;
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return TypeSafeImageFormat.Jpeg;
        }

        return null;
    }
}
