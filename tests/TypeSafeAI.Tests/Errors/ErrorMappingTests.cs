using System.Net;
using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Errors;

public class ErrorMappingTests
{
    [Test]
    [Arguments(400, typeof(TypeSafeBadRequestException))]
    [Arguments(401, typeof(TypeSafeAuthenticationException))]
    [Arguments(403, typeof(TypeSafePermissionDeniedException))]
    [Arguments(404, typeof(TypeSafeNotFoundException))]
    [Arguments(422, typeof(TypeSafeUnprocessableEntityException))]
    [Arguments(429, typeof(TypeSafeRateLimitException))]
    [Arguments(500, typeof(TypeSafeInternalServerException))]
    [Arguments(529, typeof(TypeSafeInternalServerException))]
    [Arguments(418, typeof(TypeSafeUnexpectedStatusException))]
    public async Task Status_codes_map_to_exception_types(int status, Type expected)
    {
        var handler = new FakeHttpMessageHandler().Json((HttpStatusCode)status, """{"error":{"type":"some_error","message":"nope"}}""",
            new Dictionary<string, string> { ["x-typesafe-request-id"] = "req_err" });
        using var client = TestClient.Create(handler, o => o.MaxRetries = 0);

        var ex = await Assert.ThrowsAsync<TypeSafeApiException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(ex.GetType()).IsEqualTo(expected);
        await Assert.That((int)ex.StatusCode).IsEqualTo(status);
        await Assert.That(ex.RequestId).IsEqualTo("req_err");
        await Assert.That(ex.ErrorType).IsEqualTo("some_error");
        await Assert.That(ex.ErrorMessage).IsEqualTo("nope");
        await Assert.That(ex.Message).Contains("nope");
        await Assert.That(ex.Message).Contains("req_err");
        await Assert.That(ex.Headers.ContainsKey("x-typesafe-request-id")).IsTrue();
    }

    [Test]
    public async Task Overloaded_flag_is_set_for_529()
    {
        var handler = new FakeHttpMessageHandler().Json((HttpStatusCode)529, "");
        using var client = TestClient.Create(handler, o => o.MaxRetries = 0);

        var ex = await Assert.ThrowsAsync<TypeSafeInternalServerException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(ex.IsOverloaded).IsTrue();
        await Assert.That(ex.Message).Contains("overloaded");
    }

    [Test]
    public async Task Plain_text_bodies_become_the_message()
    {
        var handler = new FakeHttpMessageHandler().Json(HttpStatusCode.BadRequest, "malformed json");
        using var client = TestClient.Create(handler, o => o.MaxRetries = 0);

        var ex = await Assert.ThrowsAsync<TypeSafeBadRequestException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));

        await Assert.That(ex.ErrorMessage).IsEqualTo("malformed json");
        await Assert.That(ex.Body).IsEqualTo("malformed json");
    }

    [Test]
    public async Task Missing_api_key_fails_at_construction()
    {
        var ex = await Assert.ThrowsAsync<TypeSafeException>(async () =>
        {
            await Task.Yield();
            using var _ = new TypeSafeClient(new HttpClient(new FakeHttpMessageHandler()), new TypeSafeClientOptions());
        });

        await Assert.That(ex.Message).Contains("TYPESAFE_API_KEY");
    }
}
