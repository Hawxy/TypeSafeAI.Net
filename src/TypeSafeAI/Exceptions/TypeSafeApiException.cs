using System.Net;
using System.Text.Json;
using TypeSafeAI.Json;

namespace TypeSafeAI;

/// <summary>The API returned a non-success status code.</summary>
public class TypeSafeApiException : TypeSafeException
{
    /// <summary>Creates the exception.</summary>
    protected TypeSafeApiException(HttpStatusCode statusCode, string message, string? requestId)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The response headers, case-insensitive.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; private set; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The raw response body, or <see langword="null"/> when empty.</summary>
    public string? Body { get; private set; }

    /// <summary>The error type reported by the API, when the body carried one.</summary>
    public string? ErrorType { get; private set; }

    /// <summary>The error message reported by the API, when the body carried one.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>How long the API asked to wait before retrying, from <c>Retry-After</c> or <c>retry-after-ms</c>, when it said.</summary>
    public TimeSpan? RetryAfter { get; private set; }

    /// <summary>Builds the exception subclass that matches the status code.</summary>
    public static TypeSafeApiException Create(
        HttpStatusCode statusCode,
        string? body,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string? requestId,
        TimeSpan? retryAfter = null)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var (errorType, errorMessage) = ParseBody(body);
        var status = (int)statusCode;
        var message = $"TypeSafe API request failed with status {status}: {errorMessage ?? DefaultMessage(status)}";

        TypeSafeApiException exception = status switch
        {
            400 => new TypeSafeBadRequestException(message, requestId),
            401 => new TypeSafeAuthenticationException(message, requestId),
            403 => new TypeSafePermissionDeniedException(message, requestId),
            404 => new TypeSafeNotFoundException(message, requestId),
            422 => new TypeSafeUnprocessableEntityException(message, requestId),
            429 => new TypeSafeRateLimitException(message, requestId),
            >= 500 and <= 599 => new TypeSafeInternalServerException(statusCode, message, requestId),
            _ => new TypeSafeUnexpectedStatusException(statusCode, message, requestId),
        };

        exception.Headers = headers;
        exception.Body = body;
        exception.ErrorType = errorType;
        exception.ErrorMessage = errorMessage;
        exception.RetryAfter = retryAfter;
        return exception;
    }

    private static (string? Type, string? Message) ParseBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize(body, TypeSafeJsonContext.Default.ErrorEnvelope);
            return envelope?.Describe() ?? (null, null);
        }
        catch (JsonException)
        {
            // Plain-text bodies are kept verbatim as the message.
            return (null, body.Length > 500 ? body[..500] : body);
        }
    }

    private static string DefaultMessage(int status) => status switch
    {
        400 => "bad request",
        401 => "invalid or missing API key",
        403 => "permission denied",
        404 => "not found",
        422 => "request validation failed",
        429 => "rate limit exceeded",
        529 => "service overloaded",
        >= 500 => "internal server error",
        _ => "unexpected status",
    };
}

/// <summary>400: the request was malformed.</summary>
public sealed class TypeSafeBadRequestException(string message, string? requestId = null)
    : TypeSafeApiException(HttpStatusCode.BadRequest, message, requestId);

/// <summary>401: the API key is missing or invalid.</summary>
public sealed class TypeSafeAuthenticationException(string message, string? requestId = null)
    : TypeSafeApiException(HttpStatusCode.Unauthorized, message, requestId);

/// <summary>403: the API key is not allowed to perform this request.</summary>
public sealed class TypeSafePermissionDeniedException(string message, string? requestId = null)
    : TypeSafeApiException(HttpStatusCode.Forbidden, message, requestId);

/// <summary>404: the resource does not exist.</summary>
public sealed class TypeSafeNotFoundException(string message, string? requestId = null)
    : TypeSafeApiException(HttpStatusCode.NotFound, message, requestId);

/// <summary>422: the request failed validation.</summary>
public sealed class TypeSafeUnprocessableEntityException(string message, string? requestId = null)
    : TypeSafeApiException(HttpStatusCode.UnprocessableEntity, message, requestId);

/// <summary>429: the rate limit was exceeded.</summary>
public sealed class TypeSafeRateLimitException(string message, string? requestId = null)
    : TypeSafeApiException(HttpStatusCode.TooManyRequests, message, requestId);

/// <summary>5xx: the API failed or is overloaded.</summary>
public sealed class TypeSafeInternalServerException(HttpStatusCode statusCode, string message, string? requestId = null)
    : TypeSafeApiException(statusCode, message, requestId)
{
    /// <summary>True for status 529, which the API uses when it is temporarily overloaded.</summary>
    public bool IsOverloaded => (int)StatusCode == 529;
}

/// <summary>Any other non-success status.</summary>
public sealed class TypeSafeUnexpectedStatusException(HttpStatusCode statusCode, string message, string? requestId = null)
    : TypeSafeApiException(statusCode, message, requestId);
