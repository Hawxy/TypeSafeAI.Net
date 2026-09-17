using System.Globalization;

namespace TypeSafeAI;

// Reads the server-requested delay. retry-after-ms wins over Retry-After, which may be seconds or an HTTP date.
internal static class RetryAfterParser
{
    public static TimeSpan? Parse(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, DateTimeOffset now)
    {
        if (TryFirst(headers, "retry-after-ms", out var ms) &&
            double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds) &&
            milliseconds >= 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        if (TryFirst(headers, "Retry-After", out var retryAfter))
        {
            if (double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            if (DateTimeOffset.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                var delay = date - now;
                return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            }
        }

        return null;
    }

    private static bool TryFirst(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string name, out string value)
    {
        if (headers.TryGetValue(name, out var values) && values.Count > 0)
        {
            value = values[0].Trim();
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }
}
