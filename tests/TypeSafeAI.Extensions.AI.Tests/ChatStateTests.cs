using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI.Tests;

public class ChatStateTests
{
    [Test]
    public async Task Builds_conversation_latest_user_message_and_response()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.System, "Be brief."),
            new(ChatRole.User, "Hi"),
            new(ChatRole.Assistant, "Hello"),
            new(ChatRole.User, [new TextContent("Where "), new TextContent("is my order?"), new DataContent(new byte[] { 1 }, "image/png")]),
        ];
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "On its way."));

        var state = ChatState.FromMessages(messages, response, new Dictionary<string, string> { ["policy"] = "ship in 2 days" }).Node!;

        var conversation = state["conversation"]!.AsArray();
        await Assert.That(conversation.Count).IsEqualTo(4);
        await Assert.That(conversation[0]!["role"]!.GetValue<string>()).IsEqualTo("system");
        await Assert.That(conversation[3]!["text"]!.GetValue<string>()).IsEqualTo("Where is my order?");
        await Assert.That(state["latest_user_message"]!.GetValue<string>()).IsEqualTo("Where is my order?");
        await Assert.That(state["response"]!.GetValue<string>()).IsEqualTo("On its way.");
        await Assert.That(state["context"]!["policy"]!.GetValue<string>()).IsEqualTo("ship in 2 days");
        await Assert.That(ChatState.LatestUserText(messages)).IsEqualTo("Where is my order?");
        await Assert.That(ChatState.LatestUserText([new ChatMessage(ChatRole.System, "x")])).IsNull();
    }
}
