using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Client;

public class HeadersAndOptionsTests
{
    [Test]
    public async Task Sends_auth_sdk_and_custom_headers()
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler, o =>
        {
            o.DefaultHeaders["X-Default"] = "d";
            o.DefaultHeaders["X-Override"] = "default";
            o.BaseUrl = new Uri("https://example.test/base/");
        });

        await client.SystemOneAsync(TestClient.UrgencyRequest(), new RequestOptions
        {
            ExtraHeaders = new Dictionary<string, string> { ["X-Extra"] = "e", ["X-Override"] = "request" },
        });

        var request = handler.Requests[0];
        await Assert.That(request.Uri.ToString()).IsEqualTo("https://example.test/v1/systemone");
        await Assert.That(request.Headers["Authorization"]).IsEqualTo("Bearer test-key");
        await Assert.That(request.Headers["Accept"]).IsEqualTo("application/json");
        await Assert.That(request.Headers["User-Agent"]).StartsWith("TypeSafeAI/");
        await Assert.That(request.Headers["X-TypeSafe-SDK"]).StartsWith("dotnet/");
        await Assert.That(request.Headers["X-TypeSafe-Runtime"]).Contains(".NET");
        await Assert.That(request.Headers["X-Default"]).IsEqualTo("d");
        await Assert.That(request.Headers["X-Extra"]).IsEqualTo("e");
        await Assert.That(request.Headers["X-Override"]).IsEqualTo("request");
    }

    [Test]
    public async Task Api_key_constructor_uses_defaults()
    {
        using var client = new TypeSafeClient("explicit-key");
        await Assert.That(client.Options.ApiKey).IsEqualTo("explicit-key");
        await Assert.That(client.Options.BaseUrl).IsEqualTo(new Uri(TypeSafeClientOptions.DefaultBaseUrl));
        await Assert.That(client.Options.DefaultModel).IsEqualTo(TypeSafeClientOptions.DefaultModelName);
    }

    [Test]
    public async Task Options_validate_timeout_retries_and_base_url()
    {
        var handler = new FakeHttpMessageHandler();

        await Assert.ThrowsAsync<TypeSafeException>(async () =>
        {
            await Task.Yield();
            using var _ = new TypeSafeClient(new HttpClient(handler), new TypeSafeClientOptions { ApiKey = "k", Timeout = TimeSpan.Zero });
        });
        await Assert.ThrowsAsync<TypeSafeException>(async () =>
        {
            await Task.Yield();
            using var _ = new TypeSafeClient(new HttpClient(handler), new TypeSafeClientOptions { ApiKey = "k", MaxRetries = -1 });
        });
        await Assert.ThrowsAsync<TypeSafeException>(async () =>
        {
            await Task.Yield();
            using var _ = new TypeSafeClient(new HttpClient(handler), new TypeSafeClientOptions { ApiKey = "k", DefaultModel = "" });
        });
    }
}
