using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using TypeSafeAI;
using TypeSafeAI.Extensions.AI.Evaluation;

// Evaluation report: score canned assistant responses with TypeSafe judgments through the
// Microsoft.Extensions.AI.Evaluation reporting pipeline, then render the report with
// `dotnet tool install -g Microsoft.Extensions.AI.Evaluation.Console` and `aieval report -p <storage> -o report.html`.

using var client = new TypeSafeClient();

var questions = new QuestionSet();
questions.Noul("Is the assistant response grounded in the supplied policy text?", id: "Grounded");
questions.Choice("What tone does the assistant response use?", ["professional", "casual", "rude"], id: "Tone");
questions.Score("How completely does the assistant response answer the user's question?", ["does not answer", "partially answers", "fully answers"], id: "Completeness");

var evaluator = new TypeSafeEvaluator(client, questions, new TypeSafeEvaluatorOptions
{
    Interpret = TypeSafeInterpretations.ByQuestion(new Dictionary<string, Func<TypeSafeMetricContext, EvaluationMetricInterpretation?>>
    {
        ["Grounded"] = TypeSafeInterpretations.NoulAtLeast(0.7),
        ["Tone"] = TypeSafeInterpretations.ChoiceIn("professional", "casual"),
        ["Completeness"] = TypeSafeInterpretations.ScoreAtLeast(1),
    }),
});

var storage = Path.Combine(AppContext.BaseDirectory, "eval-results");
var reporting = DiskBasedReportingConfiguration.Create(storage, [evaluator], executionName: DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

var policy = new PolicyContext("Refunds are available within 30 days of purchase. After 30 days we offer store credit only.");
(string Scenario, string Question, string Answer)[] cases =
[
    ("refund.in-window", "Can I get a refund? I bought it 10 days ago.", "Yes. Purchases made within the last 30 days are eligible for a full refund."),
    ("refund.out-of-window", "Can I get a refund? I bought it 3 months ago.", "Sure, send it back and we'll refund you in full."),
    ("refund.rude", "Can I get a refund?", "Read the policy yourself, it's not my job."),
];

foreach (var (scenario, question, answer) in cases)
{
    await using var run = await reporting.CreateScenarioRunAsync(scenario);
    var result = await run.EvaluateAsync(
        [new ChatMessage(ChatRole.User, question)],
        new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)),
        additionalContext: [policy]);

    Console.WriteLine(scenario);
    foreach (var metric in result.Metrics.Values)
    {
        var value = metric switch
        {
            NumericMetric n => n.Value?.ToString("F2"),
            BooleanMetric b => b.Value?.ToString(),
            StringMetric s => s.Value,
            _ => null,
        };
        var verdict = metric.Interpretation is null ? string.Empty : metric.Interpretation.Failed ? " FAIL" : " pass";
        Console.WriteLine($"  {metric.Name,-14} {value,-8}{verdict}");
    }
}

Console.WriteLine();
Console.WriteLine($"Results stored under {storage}");

sealed class PolicyContext(string text) : EvaluationContext("policy", new TextContent(text));
