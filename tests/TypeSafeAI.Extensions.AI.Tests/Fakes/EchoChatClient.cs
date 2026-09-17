using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI.Tests.Fakes;

/// <summary>A chat client that replies with a fixed text and records what it was asked.</summary>
public sealed class EchoChatClient(string reply = "echo") : IChatClient
{
    public string Reply { get; } = reply;

    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public int StreamingCalls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        StreamingCalls++;
        foreach (var word in Reply.Split(' '))
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
