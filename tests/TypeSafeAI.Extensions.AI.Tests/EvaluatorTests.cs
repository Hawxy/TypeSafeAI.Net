using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using TypeSafeAI.Extensions.AI.Evaluation;
using TypeSafeAI.Extensions.AI.Tests.Fakes;

namespace TypeSafeAI.Extensions.AI.Tests;

public class EvaluatorTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "What is the refund policy?")];
    private static readonly ChatResponse Response = new(new ChatMessage(ChatRole.Assistant, "Refunds are available within 30 days."));

    private static QuestionSet Questions()
    {
        var q = new QuestionSet();
        q.Noul("Is the response grounded in the context?", id: "grounded");
        q.Choice("What tone does the response use?", ["formal", "casual", "rude"], id: "tone");
        q.Score("How complete is the response?", ["missing", "partial", "complete"], id: "completeness");
        return q;
    }

    [Test]
    public async Task Maps_answers_to_metrics_with_metadata_and_context()
    {
        var typeSafe = FakeTypeSafeClient.Answering(
            ("grounded", FakeTypeSafeClient.Noul(0.85)),
            ("tone", FakeTypeSafeClient.Choice("formal", 0.7, ("formal", 0.7), ("casual", 0.3))),
            ("completeness", FakeTypeSafeClient.Score(1.8, 0.75, 0.0, 0.2, 0.8)));
        var options = new TypeSafeEvaluatorOptions
        {
            MetricNames = { ["completeness"] = "Completeness (TypeSafe)" },
            Interpret = TypeSafeInterpretations.ByQuestion(new Dictionary<string, Func<TypeSafeMetricContext, EvaluationMetricInterpretation?>>
            {
                ["grounded"] = TypeSafeInterpretations.NoulAtLeast(0.8),
                ["tone"] = TypeSafeInterpretations.ChoiceIn("formal", "casual"),
                ["completeness"] = TypeSafeInterpretations.ScoreAtLeast(2),
            }),
        };
        var evaluator = new TypeSafeEvaluator(typeSafe, Questions(), options);
        var context = new PolicyContext("Refunds within 30 days, store credit after.");

        await Assert.That(evaluator.EvaluationMetricNames).IsEquivalentTo(["grounded", "tone", "Completeness (TypeSafe)"]);

        var result = await evaluator.EvaluateAsync(Messages, Response, additionalContext: [context]);

        var grounded = result.Get<NumericMetric>("grounded");
        await Assert.That(grounded.Value).IsEqualTo(0.85);
        await Assert.That(grounded.Interpretation!.Failed).IsFalse();
        await Assert.That(grounded.Interpretation.Rating).IsEqualTo(EvaluationRating.Good);
        await Assert.That(grounded.Metadata!["typesafe.model"]).IsEqualTo("jev-test");
        await Assert.That(grounded.Metadata["typesafe.question_type"]).IsEqualTo("noul");
        await Assert.That(grounded.Metadata["typesafe.usage.input_tokens"]).IsEqualTo("100");
        await Assert.That(grounded.Context!.ContainsKey("policy")).IsTrue();

        var tone = result.Get<StringMetric>("tone");
        await Assert.That(tone.Value).IsEqualTo("formal");
        await Assert.That(tone.Metadata!["typesafe.p.casual"]).IsEqualTo("0.3");
        await Assert.That(tone.Interpretation!.Failed).IsFalse();

        var completeness = result.Get<NumericMetric>("Completeness (TypeSafe)");
        await Assert.That(completeness.Value).IsEqualTo(1.8);
        await Assert.That(completeness.Metadata!["typesafe.legend.2"]).IsEqualTo("level 2");
        await Assert.That(completeness.Metadata["typesafe.most_likely"]).IsEqualTo("2");
        await Assert.That(completeness.Interpretation!.Failed).IsTrue();

        var state = typeSafe.Requests[0].State.Node!;
        await Assert.That(state["response"]!.GetValue<string>()).IsEqualTo("Refunds are available within 30 days.");
        await Assert.That(state["context"]!["policy"]!.GetValue<string>()).IsEqualTo("Refunds within 30 days, store credit after.");
    }

    [Test]
    public async Task Boolean_noul_metrics_use_the_threshold_and_model_override_flows_to_request_options()
    {
        var typeSafe = FakeTypeSafeClient.Answering(("grounded", FakeTypeSafeClient.Noul(0.4)), ("tone", FakeTypeSafeClient.Choice("rude", 0.9, ("rude", 0.9))), ("completeness", FakeTypeSafeClient.Score(0.5, 0.5, 0.5, 0.5, 0)));
        var options = new TypeSafeEvaluatorOptions
        {
            NoulMetricKinds = { ["grounded"] = NoulMetricKind.Boolean },
            NoulThreshold = 0.3,
            Model = "jev-1.13.0",
            IncludeProbabilities = false,
        };
        var evaluator = new TypeSafeEvaluator(typeSafe, Questions(), options);

        var result = await evaluator.EvaluateAsync(Messages, Response);

        await Assert.That(result.Get<BooleanMetric>("grounded").Value).IsTrue();
        await Assert.That(result.Get<StringMetric>("tone").Metadata!.ContainsKey("typesafe.p.rude")).IsFalse();
        await Assert.That(typeSafe.Options[0]!.Model).IsEqualTo("jev-1.13.0");
    }

    [Test]
    public async Task Api_failures_become_diagnostics_instead_of_exceptions()
    {
        var typeSafe = new FakeTypeSafeClient(_ => throw new TypeSafeConnectionException("offline", null) { RequestId = "req_x" });
        var evaluator = new TypeSafeEvaluator(typeSafe, Questions());

        var result = await evaluator.EvaluateAsync(Messages, Response);

        await Assert.That(result.Metrics.Count).IsEqualTo(3);
        await Assert.That(result.ContainsDiagnostics(d => d.Severity == EvaluationDiagnosticSeverity.Error)).IsTrue();
        await Assert.That(result.Get<NumericMetric>("grounded").Value).IsNull();
        await Assert.That(result.Get<StringMetric>("tone").Diagnostics!.First().Message).Contains("req_x");
    }

    [Test]
    public async Task Missing_answers_get_error_diagnostics()
    {
        var typeSafe = FakeTypeSafeClient.Answering(("grounded", FakeTypeSafeClient.Noul(0.4)));
        var evaluator = new TypeSafeEvaluator(typeSafe, Questions());

        var result = await evaluator.EvaluateAsync(Messages, Response);

        await Assert.That(result.Get<NumericMetric>("grounded").Value).IsEqualTo(0.4);
        await Assert.That(result.Get<NumericMetric>("tone").ContainsDiagnostics(d => d.Message.Contains("no answer"))).IsTrue();
    }

    [Test]
    public async Task Generated_ids_are_rejected_unless_mapped()
    {
        var q = new QuestionSet();
        q.Noul("x");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await Task.Yield();
            _ = new TypeSafeEvaluator(FakeTypeSafeClient.Answering(), q);
        });

        var evaluator = new TypeSafeEvaluator(FakeTypeSafeClient.Answering(), q, new TypeSafeEvaluatorOptions { MetricNames = { ["q0"] = "Named" } });
        await Assert.That(evaluator.EvaluationMetricNames).IsEquivalentTo(["Named"]);
    }

    private sealed class PolicyContext(string text) : EvaluationContext("policy", new TextContent(text));
}
