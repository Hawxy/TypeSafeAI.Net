using System.ComponentModel;
using System.Text.Json.Nodes;
using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.QuestionSets;

public enum TicketCategory
{
    [Label("billing", Description = "Charges, refunds, invoices")]
    Billing,

    [Label("technical")]
    Technical,

    [Description("Anything else")]
    Other,
}

public enum Anger
{
    Calm,
    Annoyed,
    Furious,
}

public class QuestionSetTests
{
    private const string Response = """
        {
          "model": "jev-latest",
          "answers": {
            "q0": { "type": "choice", "choice": "technical", "probabilities": { "billing": 0.1, "technical": 0.8, "Other": 0.1 }, "confidence": 0.7 },
            "urgent": { "type": "noul", "noul": 0.9 },
            "q1": { "type": "score", "score": 1.6, "legend": { "0": "Calm", "1": "Annoyed", "2": "Furious" }, "probabilities": { "0": 0.1, "1": 0.2, "2": 0.7 }, "confidence": 0.6 },
            "q2": { "type": "choice", "choice": "b", "probabilities": { "a": 0.4, "b": 0.6 }, "confidence": 0.55 }
          }
        }
        """;

    [Test]
    public async Task Handles_give_typed_access_and_ids_are_generated_unless_supplied()
    {
        var q = new QuestionSet();
        var category = q.Choice<TicketCategory>("What is this ticket about?");
        var urgent = q.Noul("Does this convey urgency?", id: "urgent");
        var anger = q.Score<Anger>("How angry is the customer?");
        var ab = q.Choice("a or b?", "a", "b");

        await Assert.That(category.Id).IsEqualTo("q0");
        await Assert.That(urgent.Id).IsEqualTo("urgent");
        await Assert.That(anger.Id).IsEqualTo("q1");
        await Assert.That(ab.Id).IsEqualTo("q2");
        await Assert.That(q.Keys).IsEquivalentTo(["q0", "urgent", "q1", "q2"]);

        var handler = new FakeHttpMessageHandler().Ok(Response);
        using var client = TestClient.Create(handler);
        var result = await client.SystemOneAsync("ticket text", q);

        var c = result.Get(category);
        await Assert.That(c.Choice).IsEqualTo(TicketCategory.Technical);
        await Assert.That(c.Label).IsEqualTo("technical");
        await Assert.That(c.ProbabilityOf(TicketCategory.Other)).IsEqualTo(0.1);
        await Assert.That(c.Confidence).IsEqualTo(0.7);

        await Assert.That(result.Get(urgent).Probability).IsEqualTo(0.9);

        var a = result.Get(anger);
        await Assert.That(a.Score).IsEqualTo(1.6);
        await Assert.That(a.Nearest).IsEqualTo(Anger.Furious);
        await Assert.That(a.MostLikely).IsEqualTo(Anger.Furious);
        await Assert.That(a.ProbabilityOf(Anger.Annoyed)).IsEqualTo(0.2);
        await Assert.That(a.Legend[Anger.Calm]!.Value.Text).IsEqualTo("Calm");
        await Assert.That(a.IsAtLeast(Anger.Annoyed)).IsTrue();

        await Assert.That(result.Get(ab).Choice).IsEqualTo("b");
        await Assert.That(result["q2"].AsChoice().Confidence).IsEqualTo(0.55);
        await Assert.That(ReferenceEquals(result.Get(category), result.Get(category))).IsTrue();
    }

    [Test]
    public async Task Enum_questions_serialise_labels_and_descriptions()
    {
        var q = new QuestionSet();
        q.Choice<TicketCategory>("What?");
        q.Score<Anger>("How angry?");

        var handler = new FakeHttpMessageHandler().Ok("""{"model":"m","answers":{}}""");
        using var client = TestClient.Create(handler);
        await client.SystemOneAsync("text", q);

        var questions = JsonNode.Parse(handler.Requests[0].Body!)!["questions"]!;
        var expectedChoice = JsonNode.Parse("""{"type":"choice","instructions":"What?","criteria":{"billing":"Charges, refunds, invoices","technical":null,"Other":"Anything else"}}""");
        var expectedScore = JsonNode.Parse("""{"type":"score","instructions":"How angry?","criteria":["Calm","Annoyed","Furious"]}""");
        await Assert.That(JsonNode.DeepEquals(questions["q0"], expectedChoice)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(questions["q1"], expectedScore)).IsTrue();
    }

    [Test]
    public async Task Binding_rejects_mismatched_answers_and_unknown_labels()
    {
        var q = new QuestionSet();
        var noul = q.Noul("x", id: "a");
        var choice = q.Add("b", Question.Choice("y", "one", "two"));
        var typed = q.Choice<TicketCategory>("z", id: "c");
        var missing = q.Noul("w", id: "d");

        var handler = new FakeHttpMessageHandler().Ok("""
            {"model":"m","answers":{
              "a":{"type":"choice","choice":"one","probabilities":{"one":1},"confidence":1},
              "b":{"type":"choice","choice":"three","probabilities":{"three":1},"confidence":1},
              "c":{"type":"choice","choice":"shipping","probabilities":{"shipping":1},"confidence":1}
            }}
            """);
        using var client = TestClient.Create(handler);
        var result = await client.SystemOneAsync("text", q);

        await Assert.ThrowsAsync<TypeSafeResponseValidationException>(async () => await Task.FromResult(result.Get(noul)));
        await Assert.ThrowsAsync<TypeSafeResponseValidationException>(async () => await Task.FromResult(result.Get(choice)));
        await Assert.ThrowsAsync<TypeSafeResponseValidationException>(async () => await Task.FromResult(result.Get(typed)));
        await Assert.ThrowsAsync<TypeSafeResponseValidationException>(async () => await Task.FromResult(result.Get(missing)));
        await Assert.That(result.TryGet(missing, out _)).IsFalse();
    }

    [Test]
    public async Task Duplicate_or_blank_ids_are_rejected()
    {
        var q = new QuestionSet();
        q.Noul("x", id: "dup");

        await Assert.ThrowsAsync<ArgumentException>(async () => { await Task.Yield(); q.Noul("y", id: "dup"); });
        await Assert.ThrowsAsync<ArgumentException>(async () => { await Task.Yield(); q.Noul("y", id: " "); });
    }

    [Test]
    public async Task Generated_ids_skip_explicit_ones()
    {
        var q = new QuestionSet();
        q.Noul("x", id: "q0");
        var auto = q.Noul("y");
        await Assert.That(auto.Id).IsEqualTo("q1");
    }

    [Test]
    public async Task Enum_score_requires_matching_level_count()
    {
        var q = new QuestionSet();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await Task.Yield();
            q.Add<Anger>(null, Question.Score("x", "low", "high"));
        });
    }

    [Test]
    public async Task Question_factories_validate_criteria()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => { await Task.Yield(); Question.Score("x", "only one"); });
        await Assert.ThrowsAsync<ArgumentException>(async () => { await Task.Yield(); Question.Choice("x"); });
        await Assert.ThrowsAsync<ArgumentException>(async () => { await Task.Yield(); Question.Choice("x", "a", "a"); });
    }

    [Test]
    public async Task Enum_labels_resolve_label_description_and_order()
    {
        await Assert.That(EnumLabels<TicketCategory>.GetLabel(TicketCategory.Other)).IsEqualTo("Other");
        await Assert.That(EnumLabels<TicketCategory>.GetDescription(TicketCategory.Other)).IsEqualTo("Anything else");
        await Assert.That(EnumLabels<TicketCategory>.GetDescription(TicketCategory.Technical)).IsNull();
        await Assert.That(EnumLabels<Anger>.IndexOf(Anger.Furious)).IsEqualTo(2);
        await Assert.That(EnumLabels<Anger>.AtIndex(1)).IsEqualTo(Anger.Annoyed);
        await Assert.That(EnumLabels<TicketCategory>.TryParse("BILLING", out var parsed)).IsTrue();
        await Assert.That(parsed).IsEqualTo(TicketCategory.Billing);
        await Assert.That(EnumLabels<TicketCategory>.TryParse("nope", out _)).IsFalse();
    }

    [Test]
    public async Task Many_states_are_judged_in_input_order()
    {
        var q = new QuestionSet();
        var yes = q.Noul("is it?", id: "yes");
        var handler = new FakeHttpMessageHandler();
        for (var i = 0; i < 5; i++)
        {
            handler.Ok("""{"model":"m","answers":{"yes":{"type":"noul","noul":0.""" + i + "}}}");
        }

        using var client = TestClient.Create(handler);
        var results = await client.SystemOneManyAsync(["a", "b", "c", "d", "e"], q, maxConcurrency: 1);

        await Assert.That(results.Count).IsEqualTo(5);
        await Assert.That(results.Select(r => r.Get(yes).Probability)).IsEquivalentTo([0.0, 0.1, 0.2, 0.3, 0.4]);
        await Assert.That(handler.Requests.Select(r => JsonNode.Parse(r.Body!)!["state"]!.GetValue<string>())).IsEquivalentTo(["a", "b", "c", "d", "e"]);
    }
}
