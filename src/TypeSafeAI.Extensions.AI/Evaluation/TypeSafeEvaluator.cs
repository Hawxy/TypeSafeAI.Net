using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace TypeSafeAI.Extensions.AI.Evaluation;

/// <summary>
/// An <see cref="IEvaluator"/> that scores responses with TypeSafe judgments instead of an LLM judge:
/// noul answers become numeric or boolean metrics, choice answers string metrics, and score answers numeric metrics.
/// The <see cref="ChatConfiguration"/> parameter is ignored because no chat model is involved.
/// </summary>
public sealed class TypeSafeEvaluator : IEvaluator
{
    private const string MetadataPrefix = "typesafe.";

    private readonly ITypeSafeClient _client;
    private readonly IReadOnlyDictionary<string, Question> _questions;
    private readonly TypeSafeEvaluatorOptions _options;
    private readonly IReadOnlyDictionary<string, string> _metricNames;

    /// <summary>Creates an evaluator over a question set. Give every question an explicit id; it becomes the metric name unless overridden.</summary>
    public TypeSafeEvaluator(ITypeSafeClient client, QuestionSet questions, TypeSafeEvaluatorOptions? options = null)
        : this(client, (IReadOnlyDictionary<string, Question>)questions, options)
    {
        foreach (var handle in questions.Handles)
        {
            if (handle.HasGeneratedId && !_options.MetricNames.ContainsKey(handle.Id))
            {
                throw new ArgumentException(
                    $"Question '{handle.Id}' has a generated id. Give evaluator questions explicit ids or map them in {nameof(TypeSafeEvaluatorOptions.MetricNames)}.",
                    nameof(questions));
            }
        }
    }

    /// <summary>Creates an evaluator over questions keyed by id. Each id becomes the metric name unless overridden.</summary>
    public TypeSafeEvaluator(ITypeSafeClient client, IReadOnlyDictionary<string, Question> questions, TypeSafeEvaluatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        Internal.RequireQuestions(questions, nameof(questions));

        _client = client;
        _questions = questions;
        _options = options ?? new TypeSafeEvaluatorOptions();

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in questions.Keys)
        {
            names[id] = _options.MetricNames.TryGetValue(id, out var custom) ? custom : id;
        }

        if (names.Values.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            throw new ArgumentException("Metric names must be unique.", nameof(options));
        }

        _metricNames = names;
        EvaluationMetricNames = names.Values.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames { get; }

    /// <inheritdoc />
    public async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(modelResponse);

        var conversation = messages.AsReadOnlyList();
        var context = additionalContext?.AsReadOnlyList() ?? [];
        var input = new TypeSafeEvaluationInput(conversation, modelResponse, context);
        var state = _options.StateBuilder?.Invoke(input) ?? DefaultState(input);

        SystemOneResponse response;
        try
        {
            response = await _client.SystemOneAsync(state, _questions, _options.RequestOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (TypeSafeException ex)
        {
            return new EvaluationResult(_questions.Select(pair => Placeholder(pair.Key, pair.Value, ex.Message, context)));
        }

        var metrics = new List<EvaluationMetric>(_questions.Count);
        foreach (var (id, question) in _questions)
        {
            if (!response.Answers.TryGetValue(id, out var answer))
            {
                metrics.Add(Placeholder(id, question, $"TypeSafe returned no answer for question '{id}'.", context));
                continue;
            }

            var metric = ToMetric(id, question, answer);
            AddCommonMetadata(metric, response, question);
            metric.Interpretation = _options.Interpret?.Invoke(new TypeSafeMetricContext(id, metric.Name, question, answer, metric));
            AddContext(metric, context);
            metrics.Add(metric);
        }

        return new EvaluationResult(metrics);
    }

    // The metric type is decided by the question, so every path (answer, missing answer, failed request) reports the same shape.
    private EvaluationMetric CreateMetric(string questionId, Question question)
    {
        var name = _metricNames[questionId];
        return question switch
        {
            NoulQuestion when _options.NoulMetricKinds.TryGetValue(questionId, out var kind) && kind == NoulMetricKind.Boolean => new BooleanMetric(name),
            NoulQuestion or ScoreQuestion => new NumericMetric(name),
            _ => new StringMetric(name),
        };
    }

    private EvaluationMetric Placeholder(string questionId, Question question, string error, IReadOnlyList<EvaluationContext> context)
    {
        var metric = CreateMetric(questionId, question);
        metric.AddDiagnostics(EvaluationDiagnostic.Error(error));
        AddContext(metric, context);
        return metric;
    }

    private EvaluationMetric ToMetric(string questionId, Question question, Answer answer)
    {
        var metric = CreateMetric(questionId, question);
        switch (answer)
        {
            case NoulAnswer noul when metric is BooleanMetric boolean:
                boolean.Value = noul.IsYes(_options.NoulThreshold);
                metric.AddOrUpdateMetadata(MetadataPrefix + "probability", Format(noul.Probability));
                break;

            case NoulAnswer noul when metric is NumericMetric numeric:
                numeric.Value = noul.Probability;
                metric.AddOrUpdateMetadata(MetadataPrefix + "probability", Format(noul.Probability));
                break;

            case ChoiceAnswer choice when metric is StringMetric text:
                text.Value = choice.Choice;
                metric.AddOrUpdateMetadata(MetadataPrefix + "confidence", Format(choice.Confidence));
                if (_options.IncludeProbabilities)
                {
                    foreach (var pair in choice.Probabilities)
                    {
                        metric.AddOrUpdateMetadata(MetadataPrefix + "p." + pair.Key, Format(pair.Value));
                    }
                }

                break;

            case ScoreAnswer score when metric is NumericMetric numeric:
                numeric.Value = score.Score;
                metric.AddOrUpdateMetadata(MetadataPrefix + "confidence", Format(score.Confidence));
                metric.AddOrUpdateMetadata(MetadataPrefix + "most_likely", score.MostLikelyLevel.ToString(CultureInfo.InvariantCulture));
                foreach (var level in score.Levels)
                {
                    var index = level.Index.ToString(CultureInfo.InvariantCulture);
                    if (level.Description is { } description)
                    {
                        metric.AddOrUpdateMetadata(MetadataPrefix + "legend." + index, description.ToString());
                    }

                    if (_options.IncludeProbabilities)
                    {
                        metric.AddOrUpdateMetadata(MetadataPrefix + "p." + index, Format(level.Probability));
                    }
                }

                break;

            default:
                metric.AddDiagnostics(EvaluationDiagnostic.Warning($"Question '{questionId}' is a {question.Type} question but the API returned a {answer.Type} answer."));
                break;
        }

        return metric;
    }

    private static void AddCommonMetadata(EvaluationMetric metric, SystemOneResponse response, Question question)
    {
        metric.AddOrUpdateMetadata(MetadataPrefix + "model", response.Model);
        metric.AddOrUpdateMetadata(MetadataPrefix + "question_type", question.Type);
        if (response.RequestId is not null)
        {
            metric.AddOrUpdateMetadata(MetadataPrefix + "request_id", response.RequestId);
        }

        if (response.Usage?.InputTokens is { } input)
        {
            metric.AddOrUpdateMetadata(MetadataPrefix + "usage.input_tokens", input.ToString(CultureInfo.InvariantCulture));
        }

        if (response.Usage?.OutputTokens is { } output)
        {
            metric.AddOrUpdateMetadata(MetadataPrefix + "usage.output_tokens", output.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddContext(EvaluationMetric metric, IReadOnlyList<EvaluationContext> context)
    {
        if (context.Count > 0)
        {
            metric.AddOrUpdateContext(context);
        }
    }

    private static TypeSafeContent DefaultState(TypeSafeEvaluationInput input)
    {
        Dictionary<string, string>? context = null;
        if (input.AdditionalContext.Count > 0)
        {
            context = new Dictionary<string, string>(input.AdditionalContext.Count, StringComparer.Ordinal);
            foreach (var item in input.AdditionalContext)
            {
                var text = new StringBuilder();
                foreach (var content in item.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        text.Append(textContent.Text);
                    }
                }

                context[item.Name] = text.ToString();
            }
        }

        return ChatState.FromMessages(input.Messages, input.Response, context);
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
