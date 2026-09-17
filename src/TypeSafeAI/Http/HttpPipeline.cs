using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TypeSafeAI;

// Sends one logical request: serialises once, retries per policy, maps failures to SDK exceptions.
internal sealed class HttpPipeline(HttpClient http, TypeSafeClientOptions options, ILogger? logger)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string path,
        byte[]? body,
        JsonTypeInfo<TResponse> typeInfo,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken)
        where TResponse : TypeSafeResponse
    {
        var maxRetries = requestOptions?.MaxRetries ?? options.MaxRetries;
        var policy = requestOptions?.RetryPolicy ?? options.RetryPolicy;
        var timeout = requestOptions?.Timeout ?? options.Timeout;
        var uri = new Uri(options.BaseUrl, path);
        var started = Stopwatch.GetTimestamp();

        using var activity = TypeSafeDiagnostics.Source.StartActivity($"typesafe {path}", ActivityKind.Client);
        activity?.SetTag("http.request.method", method.Method);
        activity?.SetTag("server.address", uri.Host);
        activity?.SetTag("url.path", path);

        if (body is not null && _logger.IsEnabled(LogLevel.Trace))
        {
            Log.RequestBody(_logger, System.Text.Encoding.UTF8.GetString(body));
        }

        string? lastRequestId = null;

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.RequestStarting(_logger, method.Method, path, attempt + 1, maxRetries + 1, timeout);

            using var request = BuildRequest(method, uri, body, requestOptions);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                linked.CancelAfter(timeout);
            }

            HttpResponseMessage? response = null;
            string? responseBody = null;
            Exception? failure = null;
            var isTimeout = false;

            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
                responseBody = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                response?.Dispose();
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // Our per-attempt timer fired, or HttpClient.Timeout did.
                failure = ex;
                isTimeout = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException)
            {
                failure = ex;
            }

            using (response)
            {
                if (response is not null)
                {
                    var headers = FlattenHeaders(response);
                    var requestId = FirstHeader(headers, SdkHeaders.RequestIdHeader);
                    lastRequestId = requestId ?? lastRequestId;
                    activity?.SetTag("http.response.status_code", (int)response.StatusCode);
                    activity?.SetTag("typesafe.request_id", requestId);

                    if (_logger.IsEnabled(LogLevel.Trace) && responseBody is not null)
                    {
                        Log.ResponseBody(_logger, responseBody);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        var elapsed = Stopwatch.GetElapsedTime(started);
                        var result = Deserialize(responseBody, typeInfo, requestId);
                        result.RequestId = requestId;
                        result.Metadata = new ResponseMetadata
                        {
                            StatusCode = response.StatusCode,
                            Headers = headers,
                            Attempts = attempt + 1,
                            Elapsed = elapsed,
                        };

                        activity?.SetTag("typesafe.attempts", attempt + 1);
                        Log.RequestCompleted(_logger, method.Method, path, (int)response.StatusCode, elapsed, attempt + 1, requestId);
                        return result;
                    }

                    var retryAfter = RetryAfterParser.Parse(headers, DateTimeOffset.UtcNow);
                    var context = new RetryContext(attempt, maxRetries, response.StatusCode, null, retryAfter, false, false);
                    if (policy.ShouldRetry(context))
                    {
                        var delay = policy.GetDelay(context);
                        Log.Retrying(_logger, method.Method, path, attempt + 1, $"status {(int)response.StatusCode}", delay, requestId);
                        await policy.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var apiException = TypeSafeApiException.Create(response.StatusCode, responseBody, headers, requestId, retryAfter);
                    activity?.SetStatus(ActivityStatusCode.Error, apiException.Message);
                    Log.RequestFailed(_logger, method.Method, path, attempt + 1, $"status {(int)response.StatusCode}", requestId);
                    throw apiException;
                }
            }

            // No response: transport failure or timeout.
            var transportContext = new RetryContext(attempt, maxRetries, null, failure, null, isTimeout, !isTimeout);
            if (policy.ShouldRetry(transportContext))
            {
                var delay = policy.GetDelay(transportContext);
                Log.Retrying(_logger, method.Method, path, attempt + 1, isTimeout ? "timeout" : "connection failure", delay, lastRequestId);
                await policy.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var attempts = attempt + 1;
            TypeSafeConnectionException exception = isTimeout
                ? new TypeSafeTimeoutException($"TypeSafe {method.Method} {path} timed out after {attempts} attempt(s) with a per-attempt timeout of {timeout}.", failure)
                {
                    Timeout = timeout,
                    Attempts = attempts,
                    RequestId = lastRequestId,
                }
                : new TypeSafeConnectionException($"TypeSafe {method.Method} {path} could not reach the API after {attempts} attempt(s): {failure?.Message}", failure)
                {
                    Attempts = attempts,
                    RequestId = lastRequestId,
                };

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            Log.RequestFailed(_logger, method.Method, path, attempts, isTimeout ? "timeout" : "connection failure", lastRequestId);
            throw exception;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, byte[]? body, RequestOptions? requestOptions)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", SdkHeaders.UserAgent);
        request.Headers.TryAddWithoutValidation("X-TypeSafe-SDK", SdkHeaders.SdkHeader);
        request.Headers.TryAddWithoutValidation("X-TypeSafe-Runtime", SdkHeaders.RuntimeHeader);

        foreach (var header in options.DefaultHeaders)
        {
            SetHeader(request, header.Key, header.Value);
        }

        if (requestOptions?.ExtraHeaders is { } extra)
        {
            foreach (var header in extra)
            {
                SetHeader(request, header.Key, header.Value);
            }
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        return request;
    }

    private static void SetHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, value);
    }

    private static TResponse Deserialize<TResponse>(string? body, JsonTypeInfo<TResponse> typeInfo, string? requestId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new TypeSafeResponseValidationException("The API returned an empty body.") { RequestId = requestId, Body = body };
        }

        try
        {
            return JsonSerializer.Deserialize(body, typeInfo)
                ?? throw new TypeSafeResponseValidationException("The API returned a null body.") { RequestId = requestId, Body = body };
        }
        catch (JsonException ex)
        {
            throw new TypeSafeResponseValidationException($"The API returned a body this SDK could not parse: {ex.Message}", ex)
            {
                RequestId = requestId,
                Body = body,
            };
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> FlattenHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        return headers;
    }

    private static string? FirstHeader(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;
}
