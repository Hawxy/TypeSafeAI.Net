using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>
/// Turns chat messages into TypeSafe state. The shape is a JSON object with a <c>conversation</c> array of
/// <c>{ role, text }</c> entries, the <c>latest_user_message</c>, and, when supplied, the assistant <c>response</c>.
/// Only text content is included.
/// </summary>
public static class ChatState
{
    /// <summary>Builds state from a conversation and an optional response.</summary>
    public static TypeSafeContent FromMessages(IEnumerable<ChatMessage> messages, ChatResponse? response = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var state = BuildObject(messages, response);
        return TypeSafeContent.FromNode(state);
    }

    /// <summary>Builds state from a conversation, a response, and named extra context such as retrieved passages or policy text.</summary>
    public static TypeSafeContent FromMessages(IEnumerable<ChatMessage> messages, ChatResponse? response, IReadOnlyDictionary<string, string> context)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);
        var state = BuildObject(messages, response);
        if (context.Count > 0)
        {
            var contextObject = new JsonObject();
            foreach (var pair in context)
            {
                contextObject[pair.Key] = pair.Value;
            }

            state["context"] = contextObject;
        }

        return TypeSafeContent.FromNode(state);
    }

    /// <summary>Gets the text of the most recent user message, or <see langword="null"/> when there is none.</summary>
    public static string? LatestUserText(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ChatMessage? latest = null;
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                latest = message;
            }
        }

        return latest is null ? null : Text(latest);
    }

    /// <summary>Gets the text of a message, joining every text part.</summary>
    public static string Text(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return string.Concat(message.Contents.OfType<TextContent>().Select(t => t.Text));
    }

    /// <summary>Gets the text of a response, joining every text part of every message.</summary>
    public static string Text(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return string.Concat(response.Messages.Select(Text));
    }

    private static JsonObject BuildObject(IEnumerable<ChatMessage> messages, ChatResponse? response)
    {
        var conversation = new JsonArray();
        string? latestUser = null;
        foreach (var message in messages)
        {
            var text = Text(message);
            if (text.Length == 0)
            {
                continue;
            }

            conversation.Add((JsonNode)new JsonObject { ["role"] = message.Role.Value, ["text"] = text });
            if (message.Role == ChatRole.User)
            {
                latestUser = text;
            }
        }

        var state = new JsonObject { ["conversation"] = conversation };
        if (latestUser is not null)
        {
            state["latest_user_message"] = latestUser;
        }

        if (response is not null)
        {
            state["response"] = Text(response);
        }

        return state;
    }
}
