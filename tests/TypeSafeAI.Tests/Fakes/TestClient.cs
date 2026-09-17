namespace TypeSafeAI.Tests.Fakes;

public static class TestClient
{
    public const string NoulResponse = """
        {"model":"jev-latest","answers":{"is_urgent":{"type":"noul","noul":0.92}},"usage":{"input_tokens":312,"output_tokens":48}}
        """;

    public static TypeSafeClient Create(FakeHttpMessageHandler handler, Action<TypeSafeClientOptions>? configure = null, RetryPolicy? policy = null)
    {
        var options = new TypeSafeClientOptions
        {
            ApiKey = "test-key",
            RetryPolicy = policy ?? new RecordingRetryPolicy(),
        };
        configure?.Invoke(options);
        return new TypeSafeClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, options);
    }

    public static SystemOneRequest UrgencyRequest() => new()
    {
        State = "Help! My payouts have been failing for 3 days.",
        Questions = new Dictionary<string, Question>
        {
            ["is_urgent"] = Question.Noul("Does this convey urgency?", yes: "Explicitly time-sensitive", no: "No urgency expressed"),
        },
    };
}
