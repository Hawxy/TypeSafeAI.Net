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
        await Assert.That(Parse((name, value))).IsEqualTo(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Test]
    public async Task Parses_http_date_relative_to_now()
    {
        await Assert.That(Parse(("Retry-After", "Thu, 17 Sep 2026 12:00:30 GMT"))).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task Past_dates_clamp_to_zero_and_garbage_is_ignored()
    {
        await Assert.That(Parse(("Retry-After", "Thu, 17 Sep 2026 11:00:00 GMT"))).IsEqualTo(TimeSpan.Zero);
        await Assert.That(Parse(("Retry-After", "soon"))).IsNull();
        await Assert.That(Parse(("retry-after-ms", "-5"))).IsNull();
        await Assert.That(Parse()).IsNull();
    }

    private static TimeSpan? Parse(params (string Name, string Value)[] headers)
    {
        var lookup = headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase);
        return RetryAfterParser.Parse(lookup.GetValueOrDefault("retry-after-ms"), lookup.GetValueOrDefault("Retry-After"), Now);
    }
}
