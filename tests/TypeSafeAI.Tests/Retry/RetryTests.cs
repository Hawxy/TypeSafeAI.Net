using System.Net;
using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Retry;

public class RetryTests
{
    [Test]
    public async Task Retries_on_429_and_5xx_with_exponential_backoff_then_succeeds()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler()
            .Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""")
            .Json((HttpStatusCode)529, "overloaded")
            .Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, policy: policy);

        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(handler.Requests.Count).IsEqualTo(3);
        await Assert.That(response.Metadata.Attempts).IsEqualTo(3);
        await Assert.That(policy.Delays).IsEquivalentTo([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)]);
    }

    [Test]
    public async Task Gives_up_after_max_retries_and_throws_typed_exception()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler()
            .Json(HttpStatusCode.InternalServerError, "boom")
            .Json(HttpStatusCode.InternalServerError, "boom")
            .Json(HttpStatusCode.InternalServerError, "boom");
        using var client = TestClient.Create(handler, policy: policy);

        var ex = await Assert.ThrowsAsync<TypeSafeInternalServerException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(handler.Requests.Count).IsEqualTo(3);
        await Assert.That(ex.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(ex.Body).IsEqualTo("boom");
        await Assert.That(ex.IsOverloaded).IsFalse();
    }

    [Test]
    public async Task Does_not_retry_4xx_other_than_408_and_429()
    {
        var handler = new FakeHttpMessageHandler().Json(HttpStatusCode.UnprocessableEntity, """{"detail":"questions must not be empty"}""");
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<TypeSafeUnprocessableEntityException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(ex.ErrorMessage).IsEqualTo("questions must not be empty");
    }

    [Test]
    public async Task Retry_after_ms_header_beats_retry_after_and_backoff()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler()
            .Json(HttpStatusCode.TooManyRequests, "", new Dictionary<string, string> { ["Retry-After"] = "5", ["retry-after-ms"] = "250" })
            .Json(HttpStatusCode.TooManyRequests, "", new Dictionary<string, string> { ["Retry-After"] = "2" })
            .Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, policy: policy);

        await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(policy.Delays).IsEquivalentTo([TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(2)]);
    }

    [Test]
    public async Task Retry_after_beyond_cap_falls_back_to_backoff()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler()
            .Json(HttpStatusCode.TooManyRequests, "", new Dictionary<string, string> { ["Retry-After"] = "600" })
            .Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, policy: policy);

        await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(policy.Delays).IsEquivalentTo([TimeSpan.FromMilliseconds(500)]);
    }

    [Test]
    public async Task Jitter_subtracts_a_fraction_of_the_delay()
    {
        var policy = new RecordingRetryPolicy { Random = 1.0 };
        var handler = new FakeHttpMessageHandler()
            .Json(HttpStatusCode.ServiceUnavailable, "")
            .Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, policy: policy);

        await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(policy.Delays[0]).IsEqualTo(TimeSpan.FromMilliseconds(375));
    }

    [Test]
    public async Task Backoff_is_capped_at_max_delay()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler();
        for (var i = 0; i < 5; i++)
        {
            handler.Json(HttpStatusCode.BadGateway, "");
        }

        handler.Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, o => o.MaxRetries = 5, policy);

        await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(policy.Delays).IsEquivalentTo([
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(4000), TimeSpan.FromMilliseconds(5000)]);
    }

    [Test]
    public async Task Max_retries_zero_disables_retries()
    {
        var handler = new FakeHttpMessageHandler().Json(HttpStatusCode.TooManyRequests, "", new Dictionary<string, string> { ["retry-after-ms"] = "10" });
        using var client = TestClient.Create(handler, o => o.MaxRetries = 0);

        var ex = await Assert.ThrowsAsync<TypeSafeRateLimitException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(ex.RetryAfter).IsEqualTo(TimeSpan.FromMilliseconds(10));
    }

    [Test]
    public async Task Per_request_options_override_retries()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler()
            .Json(HttpStatusCode.TooManyRequests, "")
            .Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, o => o.MaxRetries = 0, policy);

        await client.SystemOneAsync(TestClient.UrgencyRequest(), new RequestOptions { MaxRetries = 1 });

        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Connection_failures_are_retried_then_surfaced()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler()
            .Throw(new HttpRequestException("dns"))
            .Throw(new IOException("reset"))
            .Throw(new HttpRequestException("dns again"));
        using var client = TestClient.Create(handler, policy: policy);

        var ex = await Assert.ThrowsAsync<TypeSafeConnectionException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(ex).IsNotTypeOf<TypeSafeTimeoutException>();
        await Assert.That(ex.Attempts).IsEqualTo(3);
        await Assert.That(ex.InnerException!.Message).IsEqualTo("dns again");
        await Assert.That(policy.Delays.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Timeouts_are_retried_then_surfaced_as_timeout_exception()
    {
        var policy = new RecordingRetryPolicy();
        var handler = new FakeHttpMessageHandler().Hang().Hang();
        using var client = TestClient.Create(handler, o =>
        {
            o.Timeout = TimeSpan.FromMilliseconds(50);
            o.MaxRetries = 1;
        }, policy);

        var ex = await Assert.ThrowsAsync<TypeSafeTimeoutException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(ex.Attempts).IsEqualTo(2);
        await Assert.That(ex.Timeout).IsEqualTo(TimeSpan.FromMilliseconds(50));
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task User_cancellation_stops_immediately_without_retry()
    {
        var policy = new RecordingRetryPolicy();
        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler().Enqueue(async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        });
        using var client = TestClient.Create(handler, policy: policy);

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.SystemOneAsync(TestClient.UrgencyRequest(), cancellationToken: cts.Token));

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(policy.Delays.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Custom_policy_can_refuse_all_retries()
    {
        var handler = new FakeHttpMessageHandler().Json(HttpStatusCode.ServiceUnavailable, "");
        var policy = new RecordingRetryPolicy { RetryOnServerError = false };
        using var client = TestClient.Create(handler, policy: policy);

        await Assert.ThrowsAsync<TypeSafeInternalServerException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }
}
