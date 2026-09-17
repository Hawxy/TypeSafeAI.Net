using System.Text.Json.Nodes;

namespace TypeSafeAI;

/// <summary>Per-request overrides of the client options.</summary>
public sealed class RequestOptions
{
    /// <summary>The model for this request. Overrides the request body and the client default.</summary>
    public string? Model { get; set; }

    /// <summary>Timeout per attempt for this request.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Retries after the initial attempt for this request.</summary>
    public int? MaxRetries { get; set; }

    /// <summary>Retry policy for this request.</summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>Headers merged over the client's default headers.</summary>
    public IDictionary<string, string>? ExtraHeaders { get; set; }

    /// <summary>
    /// Extra top-level properties merged into the request body, for API fields this SDK does not model yet.
    /// Keys that collide with modelled fields win.
    /// </summary>
    public JsonObject? ExtraBody { get; set; }
}
