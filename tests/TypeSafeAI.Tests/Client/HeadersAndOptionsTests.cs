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
    [NotInParallel("environment")]
    public async Task Options_read_environment_variables()
    {
        Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", "env-key");
        Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", "https://env.test");
        Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", "jev-env");
        try
        {
            var options = TypeSafeClientOptions.FromEnvironment();
            await Assert.That(options.ApiKey).IsEqualTo("env-key");
            await Assert.That(options.BaseUrl).IsEqualTo(new Uri("https://env.test"));
            await Assert.That(options.DefaultModel).IsEqualTo("jev-env");

            using var client = new TypeSafeClient("explicit-key");
            await Assert.That(client.Options.ApiKey).IsEqualTo("explicit-key");
            await Assert.That(client.Options.DefaultModel).IsEqualTo("jev-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", null);
            Environment.SetEnvironmentVariable("TYPESAFE_BASE_URL", null);
            Environment.SetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL", null);
        }
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
