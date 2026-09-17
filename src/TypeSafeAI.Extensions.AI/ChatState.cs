using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>
/// Turns chat messages into TypeSafe state. The shape is a JSON object with a <c>conversation</c> array of
/// <c>{ role, text }</c> entries, the <c>latest_user_message</c>, and, when supplied, the assistant <c>response</c>
/// and named <c>context</c> entries. Only text content is included.
/// </summary>
public static class ChatState
{
    /// <summary>Builds state from a conversation, an optional response, and optional named context such as retrieved passages or policy text.</summary>
    public static TypeSafeContent FromMessages(IEnumerable<ChatMessage> messages, ChatResponse? response = null, IReadOnlyDictionary<string, string>? context = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var conversation = new JsonArray();
        string? latestUser = null;
        foreach (var message in messages)
        {
            var text = message.Text;
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
            state["response"] = response.Text;
        }

        if (context is { Count: > 0 })
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
        return messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
    }
}
