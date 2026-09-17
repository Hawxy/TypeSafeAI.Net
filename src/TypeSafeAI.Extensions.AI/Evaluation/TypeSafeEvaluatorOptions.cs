using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace TypeSafeAI.Extensions.AI.Evaluation;

/// <summary>How a noul answer becomes a metric.</summary>
public enum NoulMetricKind
{
    /// <summary>A <see cref="NumericMetric"/> holding the probability of yes.</summary>
    Numeric,

    /// <summary>A <see cref="BooleanMetric"/> that is true when the probability reaches <see cref="TypeSafeEvaluatorOptions.NoulThreshold"/>.</summary>
    Boolean,
}

/// <summary>What the evaluator saw for one metric, handed to <see cref="TypeSafeEvaluatorOptions.Interpret"/>.</summary>
public sealed class TypeSafeMetricContext
{
    internal TypeSafeMetricContext(string questionId, string metricName, Question question, Answer answer, EvaluationMetric metric)
    {
        QuestionId = questionId;
        MetricName = metricName;
        Question = question;
        Answer = answer;
        Metric = metric;
    }

    /// <summary>The question id.</summary>
    public string QuestionId { get; }

    /// <summary>The metric name.</summary>
    public string MetricName { get; }

    /// <summary>The question asked.</summary>
    public Question Question { get; }

    /// <summary>The raw answer.</summary>
    public Answer Answer { get; }

    /// <summary>The metric being produced.</summary>
    public EvaluationMetric Metric { get; }
}

/// <summary>The inputs to an evaluation, handed to <see cref="TypeSafeEvaluatorOptions.StateBuilder"/>.</summary>
public sealed class TypeSafeEvaluationInput
{
    internal TypeSafeEvaluationInput(IReadOnlyList<ChatMessage> messages, ChatResponse response, IReadOnlyList<EvaluationContext> additionalContext)
    {
        Messages = messages;
        Response = response;
        AdditionalContext = additionalContext;
    }

    /// <summary>The conversation that led to the response.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>The response under evaluation.</summary>
    public ChatResponse Response { get; }

    /// <summary>Extra context supplied to the evaluator.</summary>
    public IReadOnlyList<EvaluationContext> AdditionalContext { get; }
}

/// <summary>Configures <see cref="TypeSafeEvaluator"/>.</summary>
public sealed class TypeSafeEvaluatorOptions
{
    /// <summary>The model to use; defaults to the client's default model.</summary>
    public string? Model { get; set; }

    /// <summary>Per-request overrides for the TypeSafe call.</summary>
    public RequestOptions? RequestOptions { get; set; }

    /// <summary>Metric name per question id. Questions not listed use their id.</summary>
    public IDictionary<string, string> MetricNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>How each noul question becomes a metric, by question id. Defaults to <see cref="NoulMetricKind.Numeric"/>.</summary>
    public IDictionary<string, NoulMetricKind> NoulMetricKinds { get; } = new Dictionary<string, NoulMetricKind>(StringComparer.Ordinal);

    /// <summary>Probability at which a boolean noul metric is true.</summary>
    public double NoulThreshold { get; set; } = 0.5;

    /// <summary>Record per-label and per-level probabilities in metric metadata. On by default.</summary>
    public bool IncludeProbabilities { get; set; } = true;

    /// <summary>Assigns an interpretation (rating, pass/fail) to each metric. See <see cref="TypeSafeInterpretations"/>.</summary>
    public Func<TypeSafeMetricContext, EvaluationMetricInterpretation?>? Interpret { get; set; }

    /// <summary>Builds the judged state. Defaults to <see cref="ChatState"/> with the additional context as named text.</summary>
    public Func<TypeSafeEvaluationInput, TypeSafeContent>? StateBuilder { get; set; }
}
