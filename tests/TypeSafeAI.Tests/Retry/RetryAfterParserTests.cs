namespace TypeSafeAI.Tests.Retry;

public class RetryAfterParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("retry-after-ms", "1500", 1500)]
    [Arguments("Retry-After", "3", 3000)]
    [Arguments("Retry-After", "0", 0)]
    [Arguments("retry-after", "2.5", 2500)]
    public async Task Parses_numeric_values(string name, string value, int expectedMs)
    {
        var headers = Headers((name, value));
        await Assert.That(RetryAfterParser.Parse(headers, Now)).IsEqualTo(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Test]
    public async Task Parses_http_date_relative_to_now()
    {
        var headers = Headers(("Retry-After", "Thu, 17 Sep 2026 12:00:30 GMT"));
        await Assert.That(RetryAfterParser.Parse(headers, Now)).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task Past_dates_clamp_to_zero_and_garbage_is_ignored()
    {
        await Assert.That(RetryAfterParser.Parse(Headers(("Retry-After", "Thu, 17 Sep 2026 11:00:00 GMT")), Now)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(RetryAfterParser.Parse(Headers(("Retry-After", "soon")), Now)).IsNull();
        await Assert.That(RetryAfterParser.Parse(Headers(("retry-after-ms", "-5")), Now)).IsNull();
        await Assert.That(RetryAfterParser.Parse(Headers(), Now)).IsNull();
    }

    private static Dictionary<string, IReadOnlyList<string>> Headers(params (string Name, string Value)[] headers)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
        {
            result[name] = [value];
        }

        return result;
    }
}
