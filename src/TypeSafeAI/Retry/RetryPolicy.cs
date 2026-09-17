using System.Net;

namespace TypeSafeAI;

/// <summary>
/// Decides which failures are retried and how long to wait between attempts. The client feeds these decisions to a Polly
/// retry strategy, which performs the waits on <see cref="TypeSafeClientOptions.TimeProvider"/>.
/// Defaults match the official SDKs: exponential backoff from 500 ms to 5 s with 25 % jitter,
/// retrying 408, 429 and 5xx responses plus connection and timeout failures, honouring <c>Retry-After</c> up to 60 s.
/// </summary>
public class RetryPolicy
{
    /// <summary>The first backoff delay.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The largest backoff delay.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The factor applied to the delay after each retry.</summary>
    public double BackoffMultiplier { get; set; } = 2;

    /// <summary>Fraction of each delay randomly subtracted, from 0 to 1.</summary>
    public double JitterFactor { get; set; } = 0.25;

    /// <summary>Whether <c>Retry-After</c> and <c>retry-after-ms</c> headers override the backoff.</summary>
    public bool RespectRetryAfter { get; set; } = true;

    /// <summary>The longest server-requested delay honoured; longer values fall back to backoff.</summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Whether connection failures are retried.</summary>
    public bool RetryOnConnectionError { get; set; } = true;

    /// <summary>Whether per-attempt timeouts are retried.</summary>
    public bool RetryOnTimeout { get; set; } = true;

    /// <summary>Status codes retried in addition to every 5xx.</summary>
    public ISet<int> RetryStatusCodes { get; } = new HashSet<int> { 408, 429 };

    /// <summary>Whether all 5xx responses are retried.</summary>
    public bool RetryOnServerError { get; set; } = true;

    /// <summary>Decides whether the failed attempt described by <paramref name="context"/> should be retried.</summary>
    public virtual bool ShouldRetry(RetryContext context)
    {
        if (context.Attempt >= context.MaxRetries)
        {
            return false;
        }

        if (context.IsTimeout)
        {
            return RetryOnTimeout;
        }

        if (context.IsConnectionFailure)
        {
            return RetryOnConnectionError;
        }

        if (context.StatusCode is { } status)
        {
            var code = (int)status;
            return RetryStatusCodes.Contains(code) || (RetryOnServerError && code is >= 500 and <= 599);
        }

        return false;
    }

    /// <summary>Computes how long to wait before the next attempt.</summary>
    public virtual TimeSpan GetDelay(RetryContext context)
    {
        if (RespectRetryAfter && context.RetryAfter is { } retryAfter && retryAfter >= TimeSpan.Zero && retryAfter <= MaxRetryAfter)
        {
            return retryAfter;
        }

        var delay = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, context.Attempt);
        delay = Math.Min(delay, MaxDelay.TotalMilliseconds);
        var jitter = Math.Clamp(JitterFactor, 0, 1) * NextRandom();
        return TimeSpan.FromMilliseconds(Math.Max(0, delay * (1 - jitter)));
    }

    /// <summary>Returns a value in [0, 1) used for jitter. Override for deterministic tests.</summary>
    protected virtual double NextRandom() => Random.Shared.NextDouble();
}

/// <summary>Describes a failed attempt for <see cref="RetryPolicy"/>.</summary>
/// <param name="Attempt">Zero-based number of the attempt that failed.</param>
/// <param name="MaxRetries">Retries allowed after the initial attempt.</param>
/// <param name="StatusCode">The HTTP status when a response was received.</param>
/// <param name="Exception">The transport exception when no response was received.</param>
/// <param name="RetryAfter">The server-requested delay, when present.</param>
/// <param name="IsTimeout">True when the attempt exceeded the per-attempt timeout.</param>
public readonly record struct RetryContext(
    int Attempt,
    int MaxRetries,
    HttpStatusCode? StatusCode,
    Exception? Exception,
    TimeSpan? RetryAfter,
    bool IsTimeout)
{
    /// <summary>True when the request never got a response for a reason other than a timeout.</summary>
    public bool IsConnectionFailure => Exception is not null && !IsTimeout;

    /// <summary>A short description of the failure, for logs.</summary>
    public override string ToString() =>
        StatusCode is { } status ? $"status {(int)status}"
        : IsTimeout ? "timeout"
        : $"connection failure: {Exception?.Message}";
}
