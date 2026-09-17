using System.Globalization;
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
    }

    /// <summary>Creates an evaluator over questions keyed by id. Each id becomes the metric name unless overridden.</summary>
    public TypeSafeEvaluator(ITypeSafeClient client, IReadOnlyDictionary<string, Question> questions, TypeSafeEvaluatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(questions));
        }

        _client = client;
        _questions = questions;
        _options = options ?? new TypeSafeEvaluatorOptions();

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in questions.Keys)
        {
            var name = _options.MetricNames.TryGetValue(id, out var custom) ? custom : id;
            if (IsGeneratedId(id) && !_options.MetricNames.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Question '{id}' has a generated id. Give evaluator questions explicit ids or map them in {nameof(TypeSafeEvaluatorOptions.MetricNames)}.",
                    nameof(questions));
            }

            names[id] = name;
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

        var conversation = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var context = additionalContext as IReadOnlyList<EvaluationContext> ?? additionalContext?.ToList() ?? [];
        var input = new TypeSafeEvaluationInput(conversation, modelResponse, context);
        var state = _options.StateBuilder?.Invoke(input) ?? DefaultState(input);

        var requestOptions = _options.RequestOptions;
        if (_options.Model is not null)
        {
            requestOptions = new RequestOptions
            {
                Model = _options.Model,
                Timeout = requestOptions?.Timeout,
                MaxRetries = requestOptions?.MaxRetries,
                RetryPolicy = requestOptions?.RetryPolicy,
                ExtraHeaders = requestOptions?.ExtraHeaders,
                ExtraBody = requestOptions?.ExtraBody,
            };
        }

        SystemOneResponse response;
        try
        {
            response = await _client.SystemOneAsync(state, _questions, requestOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (TypeSafeException ex)
        {
            return Failed(ex, context);
        }

        var metrics = new List<EvaluationMetric>(_questions.Count);
        foreach (var pair in _questions)
        {
            var name = _metricNames[pair.Key];
            if (!response.Answers.TryGetValue(pair.Key, out var answer))
            {
                var missing = new NumericMetric(name);
                missing.AddDiagnostics(EvaluationDiagnostic.Error($"TypeSafe returned no answer for question '{pair.Key}'."));
                metrics.Add(missing);
                continue;
            }

            var metric = ToMetric(pair.Key, name, pair.Value, answer);
            AddCommonMetadata(metric, response, pair.Value);
            metric.Interpretation = _options.Interpret?.Invoke(new TypeSafeMetricContext(pair.Key, name, pair.Value, answer, metric));
            if (context.Count > 0)
            {
                metric.AddOrUpdateContext(context);
            }

            metrics.Add(metric);
        }

        return new EvaluationResult(metrics);
    }

    private EvaluationMetric ToMetric(string questionId, string name, Question question, Answer answer)
    {
        switch (answer)
        {
            case NoulAnswer noul:
                var kind = _options.NoulMetricKinds.TryGetValue(questionId, out var k) ? k : NoulMetricKind.Numeric;
                EvaluationMetric metric = kind == NoulMetricKind.Boolean
                    ? new BooleanMetric(name, noul.IsYes(_options.NoulThreshold))
                    : new NumericMetric(name, noul.Probability);
                metric.AddOrUpdateMetadata(MetadataPrefix + "probability", Format(noul.Probability));
                return metric;

            case ChoiceAnswer choice:
                var choiceMetric = new StringMetric(name, choice.Choice);
                choiceMetric.AddOrUpdateMetadata(MetadataPrefix + "confidence", Format(choice.Confidence));
                if (_options.IncludeProbabilities)
                {
                    foreach (var pair in choice.Probabilities)
                    {
                        choiceMetric.AddOrUpdateMetadata(MetadataPrefix + "p." + pair.Key, Format(pair.Value));
                    }
                }

                return choiceMetric;

            case ScoreAnswer score:
                var scoreMetric = new NumericMetric(name, score.Score);
                scoreMetric.AddOrUpdateMetadata(MetadataPrefix + "confidence", Format(score.Confidence));
                scoreMetric.AddOrUpdateMetadata(MetadataPrefix + "most_likely", score.MostLikelyLevel.ToString(CultureInfo.InvariantCulture));
                foreach (var level in score.Levels)
                {
                    var index = level.Index.ToString(CultureInfo.InvariantCulture);
                    if (level.Description is { } description)
                    {
                        scoreMetric.AddOrUpdateMetadata(MetadataPrefix + "legend." + index, description.ToString());
                    }

                    if (_options.IncludeProbabilities)
                    {
                        scoreMetric.AddOrUpdateMetadata(MetadataPrefix + "p." + index, Format(level.Probability));
                    }
                }

                return scoreMetric;

            default:
                var unknown = new StringMetric(name);
                unknown.AddDiagnostics(EvaluationDiagnostic.Warning($"Unsupported answer type '{answer.Type}' for question '{questionId}'."));
                return unknown;
        }
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

    private EvaluationResult Failed(TypeSafeException exception, IReadOnlyList<EvaluationContext> context)
    {
        var message = exception.RequestId is null ? exception.Message : $"{exception.Message} (request id {exception.RequestId})";
        var metrics = new List<EvaluationMetric>(_questions.Count);
        foreach (var pair in _questions)
        {
            EvaluationMetric metric = pair.Value is ChoiceQuestion ? new StringMetric(_metricNames[pair.Key]) : new NumericMetric(_metricNames[pair.Key]);
            metric.AddDiagnostics(EvaluationDiagnostic.Error(message));
            if (context.Count > 0)
            {
                metric.AddOrUpdateContext(context);
            }

            metrics.Add(metric);
        }

        return new EvaluationResult(metrics);
    }

    private static TypeSafeContent DefaultState(TypeSafeEvaluationInput input)
    {
        if (input.AdditionalContext.Count == 0)
        {
            return ChatState.FromMessages(input.Messages, input.Response);
        }

        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in input.AdditionalContext)
        {
            var text = string.Concat(item.Contents.OfType<TextContent>().Select(t => t.Text));
            context[item.Name] = text;
        }

        return ChatState.FromMessages(input.Messages, input.Response, context);
    }

    private static bool IsGeneratedId(string id) =>
        id.Length > 1 && id[0] == 'q' && id.Skip(1).All(char.IsAsciiDigit);

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
