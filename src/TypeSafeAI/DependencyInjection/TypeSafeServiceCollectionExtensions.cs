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
    /// The returned builder accepts further <see cref="HttpClient"/> configuration, such as a resilience handler
    /// (set <see cref="TypeSafeClientOptions.MaxRetries"/> to 0 when you add one).
    /// </summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<TypeSafeClientOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddTransient<ITypeSafeClient>(sp => sp.GetRequiredService<TypeSafeClient>());

        // Per-attempt timeouts are enforced by the SDK; the HttpClient must not cut retries short.
        return services
            .AddHttpClient<TypeSafeClient>(HttpClientName, http => http.Timeout = Timeout.InfiniteTimeSpan)
            .AddTypedClient((http, sp) => new TypeSafeClient(
                http,
                sp.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value,
                sp.GetService<ILogger<TypeSafeClient>>()));
    }

    /// <summary>Registers the client with an explicit API key.</summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return services.AddTypeSafeClient(options => options.ApiKey = apiKey);
    }

    /// <summary>
    /// Registers the client and binds options from a configuration section, for example <c>"TypeSafe"</c> in appsettings.json.
    /// The section is applied first, then <paramref name="configure"/>.
    /// </summary>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, IConfiguration configuration, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddTypeSafeClient(options =>
        {
            configuration.Bind(options);
            configure?.Invoke(options);
        });
    }
}
