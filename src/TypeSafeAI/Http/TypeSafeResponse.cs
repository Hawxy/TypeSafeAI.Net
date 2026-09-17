using System.Net;
using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>Base class for API responses; carries transport details alongside the parsed body.</summary>
public abstract class TypeSafeResponse
{
    private protected TypeSafeResponse()
    {
    }

    /// <summary>The <c>x-typesafe-request-id</c> header, useful when contacting support.</summary>
    [JsonIgnore]
    public string? RequestId { get; internal set; }

    /// <summary>Transport details for the successful attempt.</summary>
    [JsonIgnore]
    public ResponseMetadata Metadata { get; internal set; } = ResponseMetadata.Empty;
}

/// <summary>Transport details for a response.</summary>
public sealed class ResponseMetadata
{
    internal static ResponseMetadata Empty { get; } = new()
    {
        StatusCode = 0,
        Headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
        Attempts = 0,
        Elapsed = TimeSpan.Zero,
    };

    /// <summary>The HTTP status code.</summary>
    public required HttpStatusCode StatusCode { get; init; }

    /// <summary>The response headers, case-insensitive.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; }

    /// <summary>How many attempts were made, including the successful one.</summary>
    public required int Attempts { get; init; }

    /// <summary>Wall-clock time across all attempts.</summary>
    public required TimeSpan Elapsed { get; init; }
}
