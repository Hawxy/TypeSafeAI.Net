using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace TypeSafeAI;

// Sends one logical request through a Polly pipeline (retry around a per-attempt timeout) and maps failures to SDK exceptions.
internal sealed class HttpPipeline
{
    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json") { CharSet = "utf-8" };
    private static readonly ResiliencePropertyKey<RequestState> StateKey = new("typesafe.request");

    private readonly HttpClient _http;
    private readonly TypeSafeClientOptions _options;
    private readonly ILogger _logger;
    private readonly string _authorization;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public HttpPipeline(HttpClient http, TypeSafeClientOptions options, ILogger? logger)
    {
        _http = http;
        _options = options;
        _logger = logger ?? NullLogger.Instance;
        _authorization = "Bearer " + options.ApiKey;
        _pipeline = BuildPipeline(options.MaxRetries, options.Timeout, options.RetryPolicy);
    }

    public Uri Resolve(string path) => new(_options.BaseUrl, path);

    public async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        Uri uri,
        byte[]? body,
        JsonTypeInfo<TResponse> typeInfo,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken)
        where TResponse : TypeSafeResponse
    {
        var maxRetries = requestOptions?.MaxRetries ?? _options.MaxRetries;
        var policy = requestOptions?.RetryPolicy ?? _options.RetryPolicy;
        var timeout = requestOptions?.Timeout ?? _options.Timeout;
        var pipeline = requestOptions is { MaxRetries: not null } or { RetryPolicy: not null } or { Timeout: not null }
            ? BuildPipeline(maxRetries, timeout, policy)
            : _pipeline;

        var state = new RequestState(method, uri, body, requestOptions, maxRetries, timeout);
        var started = Stopwatch.GetTimestamp();

        using var activity = TypeSafeDiagnostics.Source.HasListeners()
            ? TypeSafeDiagnostics.Source.StartActivity("typesafe " + state.Path, ActivityKind.Client)
            : null;
        activity?.SetTag("http.request.method", method.Method);
        activity?.SetTag("server.address", uri.Host);
        activity?.SetTag("url.path", state.Path);

        if (body is not null && _logger.IsEnabled(LogLevel.Trace))
        {
            Log.RequestBody(_logger, Encoding.UTF8.GetString(body));
        }

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(StateKey, state);
        HttpResponseMessage? response = null;
        try
        {
            response = await pipeline.ExecuteAsync(static (context, pipeline) => pipeline.AttemptAsync(context), context, this).ConfigureAwait(false);

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var requestId = state.LastRequestId;
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            activity?.SetTag("typesafe.request_id", requestId);
            activity?.SetTag("typesafe.attempts", state.Attempts);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                Log.ResponseBody(_logger, Encoding.UTF8.GetString(bytes));
            }

            if (response.IsSuccessStatusCode)
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                var result = Deserialize(bytes, typeInfo, requestId);
                result.RequestId = requestId;
                result.Metadata = new ResponseMetadata
                {
                    StatusCode = response.StatusCode,
                    Headers = FlattenHeaders(response),
                    Attempts = state.Attempts,
                    Elapsed = elapsed,
                };

                Log.RequestCompleted(_logger, method.Method, state.Path, (int)response.StatusCode, elapsed, state.Attempts, requestId);
                return result;
            }

            var outcome = ToRetryContext(Outcome.FromResult(response), state.Attempts - 1, maxRetries);
            throw Fail(activity, state, outcome, TypeSafeApiException.Create(response.StatusCode, BodyText(bytes), FlattenHeaders(response), requestId, outcome.RetryAfter));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            throw;
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or OperationCanceledException)
        {
            // Either our per-attempt timeout or HttpClient.Timeout fired on every attempt.
            var outcome = ToRetryContext(Outcome.FromException<HttpResponseMessage>(ex), state.Attempts - 1, maxRetries);
            throw Fail(activity, state, outcome, new TypeSafeTimeoutException(
                $"TypeSafe {method.Method} {state.Path} timed out after {state.Attempts} attempt(s) with a per-attempt timeout of {timeout}.", ex)
            {
                Timeout = timeout,
                Attempts = state.Attempts,
                RequestId = state.LastRequestId,
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
        {
            var outcome = ToRetryContext(Outcome.FromException<HttpResponseMessage>(ex), state.Attempts - 1, maxRetries);
            throw Fail(activity, state, outcome, new TypeSafeConnectionException(
                $"TypeSafe {method.Method} {state.Path} could not reach the API after {state.Attempts} attempt(s): {ex.Message}", ex)
            {
                Attempts = state.Attempts,
                RequestId = state.LastRequestId,
            });
        }
        finally
        {
            response?.Dispose();
            ResilienceContextPool.Shared.Return(context);
        }
    }

    // One attempt. Polly owns the loop: it decides on retries, waits between them, and enforces the per-attempt timeout.
    private async ValueTask<HttpResponseMessage> AttemptAsync(ResilienceContext context)
    {
        var state = context.Properties.GetValue(StateKey, null!);
        state.Attempts++;
        Log.RequestStarting(_logger, state.Method.Method, state.Path, state.Attempts, state.MaxRetries + 1, state.Timeout);

        using var request = BuildRequest(state);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, context.CancellationToken).ConfigureAwait(false);
        state.LastRequestId = FirstHeader(response, SdkHeaders.RequestIdHeader) ?? state.LastRequestId;
        return response;
    }

    private ResiliencePipeline<HttpResponseMessage> BuildPipeline(int maxRetries, TimeSpan timeout, RetryPolicy policy)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = _options.TimeProvider };

        if (maxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = maxRetries,
                ShouldHandle = args => new ValueTask<bool>(
                    !args.Context.CancellationToken.IsCancellationRequested
                    && args.Outcome.Result?.IsSuccessStatusCode != true
                    && policy.ShouldRetry(ToRetryContext(args.Outcome, args.AttemptNumber, maxRetries))),
                DelayGenerator = args => new ValueTask<TimeSpan?>(policy.GetDelay(ToRetryContext(args.Outcome, args.AttemptNumber, maxRetries))),
                OnRetry = args =>
                {
                    var state = args.Context.Properties.GetValue(StateKey, null!);
                    Log.Retrying(_logger, state.Method.Method, state.Path, args.AttemptNumber + 1, ToRetryContext(args.Outcome, args.AttemptNumber, maxRetries), args.RetryDelay, state.LastRequestId);
                    args.Outcome.Result?.Dispose();
                    return default;
                },
            });
        }

        if (timeout != Timeout.InfiniteTimeSpan)
        {
            builder.AddTimeout(timeout);
        }

        return builder.Build();
    }

    private RetryContext ToRetryContext(Outcome<HttpResponseMessage> outcome, int attempt, int maxRetries)
    {
        if (outcome.Result is { } response)
        {
            var retryAfter = RetryAfterParser.Parse(FirstHeader(response, "retry-after-ms"), FirstHeader(response, "Retry-After"), _options.TimeProvider.GetUtcNow());
            return new RetryContext(attempt, maxRetries, response.StatusCode, null, retryAfter, IsTimeout: false);
        }

        var exception = outcome.Exception;
        return new RetryContext(attempt, maxRetries, null, exception, null, IsTimeout: exception is TimeoutRejectedException or OperationCanceledException);
    }

    private TypeSafeException Fail(Activity? activity, RequestState state, RetryContext outcome, TypeSafeException exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        Log.RequestFailed(_logger, state.Method.Method, state.Path, state.Attempts, outcome, state.LastRequestId);
        return exception;
    }

    private HttpRequestMessage BuildRequest(RequestState state)
    {
        var request = new HttpRequestMessage(state.Method, state.Uri);
        request.Headers.TryAddWithoutValidation("Authorization", _authorization);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", SdkHeaders.UserAgent);
        request.Headers.TryAddWithoutValidation("X-TypeSafe-SDK", SdkHeaders.SdkHeader);
        request.Headers.TryAddWithoutValidation("X-TypeSafe-Runtime", SdkHeaders.RuntimeHeader);

        foreach (var header in _options.DefaultHeaders)
        {
            SetHeader(request, header.Key, header.Value);
        }

        if (state.RequestOptions?.ExtraHeaders is { } extra)
        {
            foreach (var header in extra)
            {
                SetHeader(request, header.Key, header.Value);
            }
        }

        if (state.Body is { } body)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = JsonContentType;
        }

        return request;
    }

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, value);
    }

    private static TResponse Deserialize<TResponse>(byte[] bytes, JsonTypeInfo<TResponse> typeInfo, string? requestId)
    {
        if (bytes.AsSpan().TrimStart(" \t\r\n"u8).IsEmpty)
        {
            throw new TypeSafeResponseValidationException("The API returned an empty body.") { RequestId = requestId, Body = BodyText(bytes) };
        }

        try
        {
            return JsonSerializer.Deserialize(bytes, typeInfo)
                ?? throw new TypeSafeResponseValidationException("The API returned a null body.") { RequestId = requestId, Body = BodyText(bytes) };
        }
        catch (JsonException ex)
        {
            throw new TypeSafeResponseValidationException($"The API returned a body this SDK could not parse: {ex.Message}", ex)
            {
                RequestId = requestId,
                Body = BodyText(bytes),
            };
        }
    }

    private static string? BodyText(byte[]? bytes) => bytes is null or { Length: 0 } ? null : Encoding.UTF8.GetString(bytes);

    // Reads one header without forcing the framework to parse every header on the response.
    private static string? FirstHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.NonValidated.TryGetValues(name, out var values))
        {
            foreach (var value in values)
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static Dictionary<string, IReadOnlyList<string>> FlattenHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        return headers;
    }

    // Per-request state shared between the attempts of one logical request.
    private sealed class RequestState(HttpMethod method, Uri uri, byte[]? body, RequestOptions? requestOptions, int maxRetries, TimeSpan timeout)
    {
        public HttpMethod Method { get; } = method;

        public Uri Uri { get; } = uri;

        public string Path { get; } = uri.AbsolutePath;

        public byte[]? Body { get; } = body;

        public RequestOptions? RequestOptions { get; } = requestOptions;

        public int MaxRetries { get; } = maxRetries;

        public TimeSpan Timeout { get; } = timeout;

        public int Attempts { get; set; }

        public string? LastRequestId { get; set; }
    }
}
