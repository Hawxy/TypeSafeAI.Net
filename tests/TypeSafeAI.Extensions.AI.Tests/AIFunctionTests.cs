using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using TypeSafeAI.Extensions.AI.Tests.Fakes;

namespace TypeSafeAI.Extensions.AI.Tests;

public class AIFunctionTests
{
    [Test]
    public async Task Function_exposes_state_parameter_and_returns_answers_as_json()
    {
        var typeSafe = FakeTypeSafeClient.Answering(
            ("urgent", FakeTypeSafeClient.Noul(0.8)),
            ("category", FakeTypeSafeClient.Choice("billing", 0.9, ("billing", 0.9), ("other", 0.1))),
            ("anger", FakeTypeSafeClient.Score(1.2, 0.6, 0.3, 0.4, 0.3)));
        var questions = new Dictionary<string, Question>
        {
            ["urgent"] = Question.Noul("Is it urgent?"),
            ["category"] = Question.Choice("Category?", "billing", "other"),
            ["anger"] = Question.Score("Anger?", "calm", "annoyed", "furious"),
        };

        var function = TypeSafeAIFunctions.Create(typeSafe, questions, "judge_ticket", "Judges a support ticket.", "The ticket text.");

        await Assert.That(function.Name).IsEqualTo("judge_ticket");
        await Assert.That(function.Description).IsEqualTo("Judges a support ticket.");
        var schema = function.JsonSchema;
        await Assert.That(schema.GetProperty("properties").GetProperty("state").GetProperty("description").GetString()).IsEqualTo("The ticket text.");
        await Assert.That(schema.GetProperty("required")[0].GetString()).IsEqualTo("state");

        var result = await function.InvokeAsync(new AIFunctionArguments { ["state"] = "My invoice is wrong and I need it fixed today" });

        var json = (JsonObject)result!;
        await Assert.That(json["model"]!.GetValue<string>()).IsEqualTo("jev-test");
        await Assert.That(json["answers"]!["urgent"]!["probability"]!.GetValue<double>()).IsEqualTo(0.8);
        await Assert.That(json["answers"]!["category"]!["choice"]!.GetValue<string>()).IsEqualTo("billing");
        await Assert.That(json["answers"]!["anger"]!["probabilities"]!["1"]!.GetValue<double>()).IsEqualTo(0.4);
        await Assert.That(typeSafe.Requests[0].State.Text).IsEqualTo("My invoice is wrong and I need it fixed today");
    }

    [Test]
    public async Task Function_accepts_json_state_and_requires_the_argument()
    {
        var typeSafe = FakeTypeSafeClient.Answering(("q", FakeTypeSafeClient.Noul(0.5)));
        var function = TypeSafeAIFunctions.Create(typeSafe, new Dictionary<string, Question> { ["q"] = Question.Noul("?") }, "judge", "desc");

        using var document = JsonDocument.Parse("""{"message":"hi"}""");
        await function.InvokeAsync(new AIFunctionArguments { ["state"] = document.RootElement });
        await Assert.That(typeSafe.Requests[0].State.Node!["message"]!.GetValue<string>()).IsEqualTo("hi");

        await Assert.ThrowsAsync<ArgumentException>(async () => await function.InvokeAsync(new AIFunctionArguments()));
    }
}
