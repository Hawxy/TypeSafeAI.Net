using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Client;

public class DependencyInjectionTests
{
    [Test]
    public async Task Resolves_interface_and_concrete_client_with_configured_options()
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        var services = new ServiceCollection();
        services.AddTypeSafeClient(o =>
        {
            o.ApiKey = "di-key";
            o.DefaultModel = "jev-di";
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITypeSafeClient>();
        var concrete = provider.GetRequiredService<TypeSafeClient>();

        await Assert.That(concrete.Options.ApiKey).IsEqualTo("di-key");
        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(response.Model).IsEqualTo("jev-latest");
        await Assert.That(handler.Requests[0].Uri.ToString()).IsEqualTo("https://api.typesafe.ai/v1/systemone");
        await Assert.That(handler.Requests[0].Headers["Authorization"]).IsEqualTo("Bearer di-key");
    }

    [Test]
    public async Task Binds_options_from_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TypeSafe:ApiKey"] = "cfg-key",
                ["TypeSafe:DefaultModel"] = "jev-cfg",
                ["TypeSafe:MaxRetries"] = "5",
                ["TypeSafe:Timeout"] = "00:00:03",
                ["TypeSafe:RetryPolicy:MaxDelay"] = "00:00:09",
                ["TypeSafe:DefaultHeaders:X-Team"] = "search",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTypeSafeClient(configuration.GetSection("TypeSafe"), o => o.DefaultHeaders["X-Extra"] = "1");
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value;
        await Assert.That(options.ApiKey).IsEqualTo("cfg-key");
        await Assert.That(options.DefaultModel).IsEqualTo("jev-cfg");
        await Assert.That(options.MaxRetries).IsEqualTo(5);
        await Assert.That(options.Timeout).IsEqualTo(TimeSpan.FromSeconds(3));
        await Assert.That(options.RetryPolicy.MaxDelay).IsEqualTo(TimeSpan.FromSeconds(9));
        await Assert.That(options.DefaultHeaders["X-Team"]).IsEqualTo("search");
        await Assert.That(options.DefaultHeaders["X-Extra"]).IsEqualTo("1");
    }

    [Test]
    public async Task Builder_accepts_a_resilience_handler()
    {
        var services = new ServiceCollection();
        services.AddTypeSafeClient(o =>
        {
            o.ApiKey = "k";
            o.MaxRetries = 0;
        }).AddStandardResilienceHandler();

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITypeSafeClient>();
        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task Missing_api_key_surfaces_when_client_is_resolved()
    {
        var services = new ServiceCollection();
        services.AddTypeSafeClient(o => o.ApiKey = null);
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<TypeSafeException>(async () =>
        {
            await Task.Yield();
            provider.GetRequiredService<ITypeSafeClient>();
        });
    }
}
