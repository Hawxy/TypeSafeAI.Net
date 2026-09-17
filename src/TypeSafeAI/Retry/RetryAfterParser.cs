using System.Globalization;

namespace TypeSafeAI;

// Reads the server-requested delay. retry-after-ms wins over Retry-After, which may be seconds or an HTTP date.
internal static class RetryAfterParser
{
    public static TimeSpan? Parse(string? retryAfterMs, string? retryAfter, DateTimeOffset now)
    {
        if (double.TryParse(retryAfterMs, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds >= 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        if (double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var delay = date - now;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }
}
