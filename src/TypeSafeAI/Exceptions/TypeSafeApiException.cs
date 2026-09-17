using System.Net;
using System.Text.Json;
using TypeSafeAI.Json;

namespace TypeSafeAI;

/// <summary>Details of a failed API response, shared by every <see cref="TypeSafeApiException"/> subclass.</summary>
public sealed class TypeSafeApiError
{
    /// <summary>The HTTP status code.</summary>
    public required HttpStatusCode StatusCode { get; init; }

    /// <summary>The exception message.</summary>
    public required string Message { get; init; }

    /// <summary>The <c>x-typesafe-request-id</c> header, when present.</summary>
    public string? RequestId { get; init; }

    /// <summary>The response headers, case-insensitive.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The raw response body, or <see langword="null"/> when empty.</summary>
    public string? Body { get; init; }

    /// <summary>The error type reported by the API, when the body carried one.</summary>
    public string? ErrorType { get; init; }

    /// <summary>The error message reported by the API, when the body carried one.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>How long the API asked to wait before retrying, when it said.</summary>
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>The API returned a non-success status code.</summary>
public class TypeSafeApiException : TypeSafeException
{
    /// <summary>Creates the exception from error details.</summary>
    public TypeSafeApiException(TypeSafeApiError error)
        : base(Check(error).Message)
    {
        Error = error;
        RequestId = error.RequestId;
    }

    /// <summary>The full error details.</summary>
    public TypeSafeApiError Error { get; }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode => Error.StatusCode;

    /// <summary>The response headers, case-insensitive.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => Error.Headers;

    /// <summary>The raw response body, or <see langword="null"/> when empty.</summary>
    public string? Body => Error.Body;

    /// <summary>The error type reported by the API, when the body carried one.</summary>
    public string? ErrorType => Error.ErrorType;

    /// <summary>The error message reported by the API, when the body carried one.</summary>
    public string? ErrorMessage => Error.ErrorMessage;

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
        var summary = errorMessage ?? DefaultMessage(status);
        var message = $"TypeSafe API request failed with status {status}: {summary}" +
                      (requestId is null ? string.Empty : $" (request id {requestId})");

        var error = new TypeSafeApiError
        {
            StatusCode = statusCode,
            Message = message,
            RequestId = requestId,
            Headers = headers,
            Body = body,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            RetryAfter = retryAfter,
        };

        return status switch
        {
            400 => new TypeSafeBadRequestException(error),
            401 => new TypeSafeAuthenticationException(error),
            403 => new TypeSafePermissionDeniedException(error),
            404 => new TypeSafeNotFoundException(error),
            422 => new TypeSafeUnprocessableEntityException(error),
            429 => new TypeSafeRateLimitException(error),
            >= 500 and <= 599 => new TypeSafeInternalServerException(error),
            _ => new TypeSafeUnexpectedStatusException(error),
        };
    }

    private static TypeSafeApiError Check(TypeSafeApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error;
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
public sealed class TypeSafeBadRequestException(TypeSafeApiError error) : TypeSafeApiException(error);

/// <summary>401: the API key is missing or invalid.</summary>
public sealed class TypeSafeAuthenticationException(TypeSafeApiError error) : TypeSafeApiException(error);

/// <summary>403: the API key is not allowed to perform this request.</summary>
public sealed class TypeSafePermissionDeniedException(TypeSafeApiError error) : TypeSafeApiException(error);

/// <summary>404: the resource does not exist.</summary>
public sealed class TypeSafeNotFoundException(TypeSafeApiError error) : TypeSafeApiException(error);

/// <summary>422: the request failed validation.</summary>
public sealed class TypeSafeUnprocessableEntityException(TypeSafeApiError error) : TypeSafeApiException(error);

/// <summary>429: the rate limit was exceeded.</summary>
public sealed class TypeSafeRateLimitException(TypeSafeApiError error) : TypeSafeApiException(error)
{
    /// <summary>How long the API asked to wait, from <c>Retry-After</c> or <c>retry-after-ms</c>.</summary>
    public TimeSpan? RetryAfter => Error.RetryAfter;
}

/// <summary>5xx: the API failed or is overloaded.</summary>
public sealed class TypeSafeInternalServerException(TypeSafeApiError error) : TypeSafeApiException(error)
{
    /// <summary>True for status 529, which the API uses when it is temporarily overloaded.</summary>
    public bool IsOverloaded => (int)StatusCode == 529;
}

/// <summary>Any other non-success status.</summary>
public sealed class TypeSafeUnexpectedStatusException(TypeSafeApiError error) : TypeSafeApiException(error);
