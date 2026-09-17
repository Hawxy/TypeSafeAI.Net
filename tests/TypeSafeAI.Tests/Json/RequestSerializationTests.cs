using System.Text.Json.Nodes;
using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Json;

public class RequestSerializationTests
{
    [Test]
    public async Task Noul_request_matches_documented_wire_shape()
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(TestClient.UrgencyRequest());

        var expected = JsonNode.Parse("""
            {
              "state": "Help! My payouts have been failing for 3 days.",
              "model": "jev-latest",
              "questions": {
                "is_urgent": {
                  "type": "noul",
                  "instructions": "Does this convey urgency?",
                  "criteria": { "true": "Explicitly time-sensitive", "false": "No urgency expressed" }
                }
              }
            }
            """);
        var actual = JsonNode.Parse(handler.Requests[0].Body!);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
        await Assert.That(handler.Requests[0].Body!).StartsWith("{\"state\":");
        await Assert.That(handler.Requests[0].Body!).Contains("\"type\":\"noul\",\"instructions\"");
    }

    [Test]
    public async Task Choice_and_score_questions_serialise_criteria_in_their_documented_shapes()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"model":"jev-latest","answers":{}}""");
        using var client = TestClient.Create(handler);

        var questions = new Dictionary<string, Question>
        {
            ["category"] = Question.Choice("What is this ticket about?", new Dictionary<string, TypeSafeContent?>
            {
                ["billing"] = "Charges, refunds, invoices",
                ["technical"] = null,
            }),
            ["anger"] = Question.Score("How angry is the customer?", "calm", "annoyed", "furious"),
            ["plain"] = Question.Noul("Is it a question?"),
        };

        await client.SystemOneAsync("some text", questions);

        var actual = JsonNode.Parse(handler.Requests[0].Body!)!;
        var expected = JsonNode.Parse("""
            {
              "state": "some text",
              "model": "jev-latest",
              "questions": {
                "category": { "type": "choice", "instructions": "What is this ticket about?", "criteria": { "billing": "Charges, refunds, invoices", "technical": null } },
                "anger": { "type": "score", "instructions": "How angry is the customer?", "criteria": ["calm", "annoyed", "furious"] },
                "plain": { "type": "noul", "instructions": "Is it a question?" }
              }
            }
            """);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task Structured_state_and_instructions_are_embedded_as_json()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"model":"jev-latest","answers":{}}""");
        using var client = TestClient.Create(handler);

        var state = TypeSafeContent.FromNode(new JsonObject { ["message"] = "hi", ["order_id"] = "A1" });
        var instructions = TypeSafeContent.FromJson("""{"field":{"name":"total","type":"money"},"question":"Is the total present?"}""");
        await client.SystemOneAsync(state, new Dictionary<string, Question> { ["q"] = Question.Noul(instructions) });

        var actual = JsonNode.Parse(handler.Requests[0].Body!)!;
        await Assert.That(actual["state"]!["order_id"]!.GetValue<string>()).IsEqualTo("A1");
        await Assert.That(actual["questions"]!["q"]!["instructions"]!["field"]!["name"]!.GetValue<string>()).IsEqualTo("total");
    }

    [Test]
    public async Task Array_state_serialises_as_string_array()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"model":"jev-latest","answers":{}}""");
        using var client = TestClient.Create(handler);

        await client.SystemOneAsync(TypeSafeContent.FromStrings(["first", "second"]), new Dictionary<string, Question> { ["q"] = Question.Noul("x") });

        var state = JsonNode.Parse(handler.Requests[0].Body!)!["state"]!.AsArray();
        await Assert.That(state.Count).IsEqualTo(2);
        await Assert.That(state[1]!.GetValue<string>()).IsEqualTo("second");
    }

    [Test]
    public async Task Extra_body_is_merged_over_modelled_fields()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"model":"jev-latest","answers":{}}""");
        using var client = TestClient.Create(handler);

        var options = new RequestOptions { ExtraBody = new JsonObject { ["experimental"] = true, ["model"] = "jev-preview" } };
        await client.SystemOneAsync(TestClient.UrgencyRequest(), options);

        var actual = JsonNode.Parse(handler.Requests[0].Body!)!;
        await Assert.That(actual["experimental"]!.GetValue<bool>()).IsTrue();
        await Assert.That(actual["model"]!.GetValue<string>()).IsEqualTo("jev-preview");
    }

    [Test]
    public async Task Model_precedence_is_request_options_then_request_then_client_default()
    {
        var handler = new FakeHttpMessageHandler()
            .Ok("""{"model":"a","answers":{}}""")
            .Ok("""{"model":"b","answers":{}}""")
            .Ok("""{"model":"c","answers":{}}""");
        using var client = TestClient.Create(handler, o => o.DefaultModel = "client-default");

        var questions = new Dictionary<string, Question> { ["q"] = Question.Noul("x") };
        await client.SystemOneAsync(new SystemOneRequest { State = "s", Questions = questions, Model = "request-model" }, new RequestOptions { Model = "option-model" });
        await client.SystemOneAsync(new SystemOneRequest { State = "s", Questions = questions, Model = "request-model" });
        await client.SystemOneAsync(new SystemOneRequest { State = "s", Questions = questions });

        await Assert.That(JsonNode.Parse(handler.Requests[0].Body!)!["model"]!.GetValue<string>()).IsEqualTo("option-model");
        await Assert.That(JsonNode.Parse(handler.Requests[1].Body!)!["model"]!.GetValue<string>()).IsEqualTo("request-model");
        await Assert.That(JsonNode.Parse(handler.Requests[2].Body!)!["model"]!.GetValue<string>()).IsEqualTo("client-default");
    }

    [Test]
    public async Task Empty_questions_or_state_are_rejected_before_sending()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = TestClient.Create(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SystemOneAsync(new SystemOneRequest { State = "s", Questions = new Dictionary<string, Question>() }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SystemOneAsync(new SystemOneRequest { State = default, Questions = new Dictionary<string, Question> { ["q"] = Question.Noul("x") } }));
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }
}
