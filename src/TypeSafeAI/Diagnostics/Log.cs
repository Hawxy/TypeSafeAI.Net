using Microsoft.Extensions.Logging;

namespace TypeSafeAI;

internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "TypeSafe {Method} {Path} starting (attempt {Attempt} of {MaxAttempts}, timeout {Timeout})")]
    public static partial void RequestStarting(ILogger logger, string method, string path, int attempt, int maxAttempts, TimeSpan timeout);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "TypeSafe {Method} {Path} completed with {StatusCode} in {Elapsed} after {Attempts} attempt(s) (request id {RequestId})")]
    public static partial void RequestCompleted(ILogger logger, string method, string path, int statusCode, TimeSpan elapsed, int attempts, string? requestId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "TypeSafe {Method} {Path} attempt {Attempt} failed with {Reason}; retrying in {Delay} (request id {RequestId})")]
    public static partial void Retrying(ILogger logger, string method, string path, int attempt, string reason, TimeSpan delay, string? requestId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "TypeSafe {Method} {Path} failed after {Attempts} attempt(s) with {Reason} (request id {RequestId})")]
    public static partial void RequestFailed(ILogger logger, string method, string path, int attempts, string reason, string? requestId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Trace, Message = "TypeSafe request body: {Body}")]
    public static partial void RequestBody(ILogger logger, string body);

    [LoggerMessage(EventId = 6, Level = LogLevel.Trace, Message = "TypeSafe response body: {Body}")]
    public static partial void ResponseBody(ILogger logger, string body);
}
