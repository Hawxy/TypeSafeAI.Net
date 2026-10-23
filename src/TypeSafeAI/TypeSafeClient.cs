using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TypeSafeAI.Json;

namespace TypeSafeAI;

/// <summary>The TypeSafe API client.</summary>
public sealed class TypeSafeClient : ITypeSafeClient, IDisposable
{
    private static readonly KeyValuePair<string, string> ImagesHeader = new(SdkHeaders.ImagesHeader, "true");
    private static readonly KeyValuePair<string, string> LongContextHeader = new(SdkHeaders.LongContextHeader, "true");

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly HttpPipeline _pipeline;
    private readonly Uri _systemOneUri;

    /// <summary>Creates a client with an API key and default settings.</summary>
    public TypeSafeClient(string apiKey)
        : this(new TypeSafeClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>Creates a client that owns its <see cref="HttpClient"/>.</summary>
    public TypeSafeClient(TypeSafeClientOptions options)
        : this(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, options, null, ownsHttpClient: true)
    {
    }

    /// <summary>Creates a client over an <see cref="HttpClient"/> you manage. The client is not disposed with this instance.</summary>
    public TypeSafeClient(HttpClient httpClient, TypeSafeClientOptions options, ILogger<TypeSafeClient>? logger = null)
        : this(httpClient, options, logger, ownsHttpClient: false)
    {
    }

    private TypeSafeClient(HttpClient httpClient, TypeSafeClientOptions options, ILogger? logger, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        Options = options;
        _pipeline = new HttpPipeline(httpClient, options, logger);
        _systemOneUri = _pipeline.Resolve("v1/systemone");
        Models = new ModelsResource(_pipeline);
    }

    /// <summary>The effective options.</summary>
    public TypeSafeClientOptions Options { get; }

    /// <inheritdoc />
    public IModelsResource Models { get; }

    /// <inheritdoc />
    public Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Questions);
        if (request.Questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(request));
        }

        if (request.State.IsEmpty)
        {
            throw new ArgumentException("The state must not be empty.", nameof(request));
        }

        var assets = new AssetCollector();
        assets.Add(request.State);
        foreach (var question in request.Questions.Values)
        {
            ArgumentNullException.ThrowIfNull(question, nameof(request));
            question.CollectAssets(assets);
        }

        var payload = new SystemOneRequestPayload
        {
            State = request.State,
            Model = options?.Model ?? request.Model ?? Options.DefaultModel,
            Questions = request.Questions,
            Assets = assets.Assets,
        };

        var longContext = options?.LongContext ?? Options.LongContext;
        KeyValuePair<string, string>[]? headers = (assets.RequiresImages, longContext) switch
        {
            (true, true) => [ImagesHeader, LongContextHeader],
            (true, false) => [ImagesHeader],
            (false, true) => [LongContextHeader],
            _ => null,
        };

        var body = new RequestBody(Serialize(payload, options?.ExtraBody), assets.Assets, headers);
        return _pipeline.SendAsync(HttpMethod.Post, _systemOneUri, body, TypeSafeJsonContext.Default.SystemOneResponse, options, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private static byte[] Serialize(SystemOneRequestPayload payload, JsonObject? extraBody)
    {
        var context = TypeSafeJsonContext.Default;
        if (extraBody is null || extraBody.Count == 0)
        {
            return JsonSerializer.SerializeToUtf8Bytes(payload, context.SystemOneRequestPayload);
        }

        var node = JsonSerializer.SerializeToNode(payload, context.SystemOneRequestPayload) as JsonObject
            ?? throw new InvalidOperationException("The request did not serialise to an object.");
        foreach (var pair in extraBody)
        {
            node[pair.Key] = pair.Value?.DeepClone();
        }

        return JsonSerializer.SerializeToUtf8Bytes(node, context.JsonObject);
    }
}
