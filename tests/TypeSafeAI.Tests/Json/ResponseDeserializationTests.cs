using TypeSafeAI.Tests.Fakes;

namespace TypeSafeAI.Tests.Json;

public class ResponseDeserializationTests
{
    [Test]
    public async Task Documented_noul_response_binds_answer_usage_and_request_id()
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse, requestId: "req_abc");
        using var client = TestClient.Create(handler);

        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(response.Model).IsEqualTo("jev-latest");
        await Assert.That(response.RequestId).IsEqualTo("req_abc");
        await Assert.That(response.Usage!.InputTokens).IsEqualTo(312);
        await Assert.That(response.Usage.OutputTokens).IsEqualTo(48);
        await Assert.That(response.Metadata.Attempts).IsEqualTo(1);

        var answer = response.Nouls["is_urgent"];
        await Assert.That(answer.Probability).IsEqualTo(0.92);
        await Assert.That(answer.IsYes()).IsTrue();
        await Assert.That(response["is_urgent"].AsNoul().Probability).IsEqualTo(0.92);
    }

    [Test]
    public async Task Choice_and_score_answers_bind_with_distributions_and_legend()
    {
        const string body = """
            {
              "model": "jev-1.13.0",
              "answers": {
                "category": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.85, "technical": 0.15 }, "confidence": 0.92 },
                "anger": { "type": "score", "score": 1.5, "legend": { "0": "calm", "1": "annoyed", "2": "furious" }, "probabilities": { "0": 0.2, "1": 0.6, "2": 0.2 }, "confidence": 0.78 }
              },
              "usage": { "input_tokens": 10, "output_tokens": 2 }
            }
            """;
        var handler = new FakeHttpMessageHandler().Ok(body);
        using var client = TestClient.Create(handler);

        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        var category = response.Choices["category"];
        await Assert.That(category.Choice).IsEqualTo("billing");
        await Assert.That(category.ProbabilityOf("technical")).IsEqualTo(0.15);
        await Assert.That(category.Confidence).IsEqualTo(0.92);
        await Assert.That(category.Ranked().First().Key).IsEqualTo("billing");

        var anger = response.Scores["anger"];
        await Assert.That(anger.Score).IsEqualTo(1.5);
        await Assert.That(anger.Legend[2]!.Value.Text).IsEqualTo("furious");
        await Assert.That(anger.Probabilities[1]).IsEqualTo(0.6);
        await Assert.That(anger.MostLikelyLevel).IsEqualTo(1);
        await Assert.That(anger.NearestLevel).IsEqualTo(2);
        await Assert.That(anger.Levels.Count).IsEqualTo(3);
        await Assert.That(anger.Levels[0].Description!.Value.Text).IsEqualTo("calm");
        await Assert.That(response.Nouls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Discriminator_may_appear_after_other_properties()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"model":"m","answers":{"q":{"noul":0.3,"type":"noul"}}}""");
        using var client = TestClient.Create(handler);

        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(response["q"].AsNoul().Probability).IsEqualTo(0.3);
    }

    [Test]
    public async Task Unmodelled_answer_properties_are_kept_in_additional_data()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"model":"m","answers":{"q":{"type":"noul","noul":0.3,"explanation":"because"}}}""");
        using var client = TestClient.Create(handler);

        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        await Assert.That(response["q"].AdditionalData!["explanation"].GetString()).IsEqualTo("because");
    }

    [Test]
    public async Task Wrong_answer_cast_reports_both_types()
    {
        var handler = new FakeHttpMessageHandler().Ok(TestClient.NoulResponse);
        using var client = TestClient.Create(handler);
        var response = await client.SystemOneAsync(TestClient.UrgencyRequest());

        var ex = await Assert.ThrowsAsync<TypeSafeResponseValidationException>(async () => await Task.FromResult(response["is_urgent"].AsChoice()));
        await Assert.That(ex.Message).Contains("choice");
        await Assert.That(ex.Message).Contains("noul");
    }

    [Test]
    public async Task Unparseable_success_body_raises_validation_error_without_retry()
    {
        var handler = new FakeHttpMessageHandler().Ok("<html>oops</html>");
        using var client = TestClient.Create(handler);

        var ex = await Assert.ThrowsAsync<TypeSafeResponseValidationException>(() => client.SystemOneAsync(TestClient.UrgencyRequest()));
        await Assert.That(ex.Body).IsEqualTo("<html>oops</html>");
        await Assert.That(ex.RequestId).IsEqualTo("req_123");
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Models_list_binds_documented_fields()
    {
        var handler = new FakeHttpMessageHandler().Ok("""{"models":[{"name":"jev-1.13.0","description":"Jev 1.13","release_date":"2026-08-01"}]}""");
        using var client = TestClient.Create(handler);

        var models = await client.Models.ListAsync();

        await Assert.That(handler.Requests[0].Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(handler.Requests[0].Uri.AbsolutePath).IsEqualTo("/v1/models");
        await Assert.That(models.Models[0].Name).IsEqualTo("jev-1.13.0");
        await Assert.That(models.Models[0].ReleaseDate).IsEqualTo("2026-08-01");
    }
}
