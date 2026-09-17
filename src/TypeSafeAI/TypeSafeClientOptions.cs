namespace TypeSafeAI;

/// <summary>Configures a <see cref="TypeSafeClient"/>.</summary>
public sealed class TypeSafeClientOptions
{
    /// <summary>Environment variable holding the API key.</summary>
    public const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>Environment variable overriding the base URL.</summary>
    public const string BaseUrlEnvironmentVariable = "TYPESAFE_BASE_URL";

    /// <summary>Environment variable overriding the default model.</summary>
    public const string DefaultModelEnvironmentVariable = "TYPESAFE_DEFAULT_MODEL";

    /// <summary>The production API root.</summary>
    public const string DefaultBaseUrl = "https://api.typesafe.ai";

    /// <summary>The alias for the most recent stable model.</summary>
    public const string DefaultModelName = "jev-latest";

    /// <summary>The API key. Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The API root. Defaults to <see cref="DefaultBaseUrl"/>.</summary>
    public Uri BaseUrl { get; set; } = new(DefaultBaseUrl);

    /// <summary>The model used when a request does not name one. Defaults to <see cref="DefaultModelName"/>.</summary>
    public string DefaultModel { get; set; } = DefaultModelName;

    /// <summary>
    /// Timeout per attempt, between 10 milliseconds and 24 hours, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.
    /// Defaults to 10 seconds. Retries each get a fresh timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Retries after the initial attempt. Defaults to 2. Set to 0 to disable, for example when using a resilience handler.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Decides which failures are retried and how long to wait.</summary>
    public RetryPolicy RetryPolicy { get; set; } = new();

    /// <summary>Headers added to every request. Per-request headers take precedence.</summary>
    public IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The clock behind retry delays and per-attempt timeouts. Replace it in tests to avoid real waits.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>Creates options populated from the <c>TYPESAFE_*</c> environment variables.</summary>
    public static TypeSafeClientOptions FromEnvironment()
    {
        var options = new TypeSafeClientOptions();
        options.ApplyEnvironment();
        return options;
    }

    /// <summary>Overrides values with any <c>TYPESAFE_*</c> environment variables that are set.</summary>
    public void ApplyEnvironment()
    {
        if (Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) is { Length: > 0 } apiKey)
        {
            ApiKey = apiKey;
        }

        if (Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable) is { Length: > 0 } baseUrl)
        {
            BaseUrl = new Uri(baseUrl, UriKind.Absolute);
        }

        if (Environment.GetEnvironmentVariable(DefaultModelEnvironmentVariable) is { Length: > 0 } model)
        {
            DefaultModel = model;
        }
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new TypeSafeException(
                $"No API key configured. Set {nameof(TypeSafeClientOptions)}.{nameof(ApiKey)} or the {ApiKeyEnvironmentVariable} environment variable.");
        }

        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri)
        {
            throw new TypeSafeException($"{nameof(BaseUrl)} must be an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(DefaultModel))
        {
            throw new TypeSafeException($"{nameof(DefaultModel)} must not be empty.");
        }

        if (Timeout != System.Threading.Timeout.InfiniteTimeSpan && (Timeout <= TimeSpan.FromMilliseconds(10) || Timeout >= TimeSpan.FromHours(24)))
        {
            throw new TypeSafeException($"{nameof(Timeout)} must be between 10 milliseconds and 24 hours, or Timeout.InfiniteTimeSpan.");
        }

        if (MaxRetries < 0)
        {
            throw new TypeSafeException($"{nameof(MaxRetries)} must not be negative.");
        }

        ArgumentNullException.ThrowIfNull(RetryPolicy);
        ArgumentNullException.ThrowIfNull(TimeProvider);
    }
}
