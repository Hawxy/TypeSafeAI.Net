using System.Net;
using System.Text;

namespace TypeSafeAI.Tests.Fakes;

/// <summary>Scripted HTTP responses with captured requests.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<CapturedRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public FakeHttpMessageHandler Json(HttpStatusCode status, string body, IDictionary<string, string>? headers = null) =>
        Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (headers is not null)
            {
                foreach (var header in headers)
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return Task.FromResult(response);
        });

    public FakeHttpMessageHandler Ok(string body, string? requestId = "req_123") =>
        Json(HttpStatusCode.OK, body, requestId is null ? null : new Dictionary<string, string> { ["x-typesafe-request-id"] = requestId });

    public FakeHttpMessageHandler Throw(Exception exception) => Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));

    /// <summary>Never completes until the per-attempt timeout or the caller cancels.</summary>
    public FakeHttpMessageHandler Hang() => Enqueue(async (_, ct) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("unreachable");
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No scripted response left.");
        }

        return await _responses.Dequeue()(request, cancellationToken);
    }
}

public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers);
