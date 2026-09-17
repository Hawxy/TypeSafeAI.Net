using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TypeSafeAI;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="TypeSafeClient"/> with <see cref="IHttpClientFactory"/>.</summary>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>The named <see cref="HttpClient"/> the client uses.</summary>
    public const string HttpClientName = "TypeSafeAI";

    /// <summary>
    /// Registers <see cref="ITypeSafeClient"/> and <see cref="TypeSafeClient"/> as typed HTTP clients.
    /// Options are read from the <c>TYPESAFE_*</c> environment variables first, then from <paramref name="configure"/>.
    /// The returned builder accepts further <see cref="HttpClient"/> configuration, such as a resilience handler
    /// (set <see cref="TypeSafeClientOptions.MaxRetries"/> to 0 when you add one).
    /// </summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddOptions<TypeSafeClientOptions>().Configure(o => o.ApplyEnvironment());
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        return services.AddTypeSafeClientCore();
    }

    /// <summary>Registers the client with an explicit API key.</summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return services.AddTypeSafeClient(o => o.ApiKey = apiKey);
    }

    /// <summary>
    /// Registers the client and binds options from a configuration section, for example <c>"TypeSafe"</c> in appsettings.json.
    /// Environment variables are applied first, then the section, then <paramref name="configure"/>.
    /// </summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, IConfiguration configuration, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services.AddOptions<TypeSafeClientOptions>()
            .Configure(o => o.ApplyEnvironment())
            .Configure(o => configuration.Bind(o));
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        return services.AddTypeSafeClientCore();
    }

    private static IHttpClientBuilder AddTypeSafeClientCore(this IServiceCollection services)
    {
        services.TryAddTransient<ITypeSafeClient>(sp => sp.GetRequiredService<TypeSafeClient>());

        return services
            .AddHttpClient<TypeSafeClient>(HttpClientName, (sp, http) =>
            {
                // Per-attempt timeouts are enforced by the SDK; the HttpClient must not cut retries short.
                var options = sp.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value;
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.BaseAddress = options.BaseUrl;
            })
            .AddTypedClient((http, sp) => new TypeSafeClient(
                http,
                sp.GetRequiredService<IOptions<TypeSafeClientOptions>>(),
                sp.GetService<ILogger<TypeSafeClient>>()));
    }
}
