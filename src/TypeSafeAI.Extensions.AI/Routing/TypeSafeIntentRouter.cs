namespace TypeSafeAI.Extensions.AI;

/// <summary>Where an intent router sends a request.</summary>
public enum RouteTarget
{
    /// <summary>The intent is clear and simple enough for deterministic code.</summary>
    Code,

    /// <summary>The intent is clear but needs a model, for example a specialist prompt.</summary>
    Model,

    /// <summary>Confidence is below the floor or the request is too complex: hand it to a person.</summary>
    Human,
}

/// <summary>An intent-routing decision.</summary>
/// <typeparam name="TIntent">The enum of intents.</typeparam>
public sealed class IntentRoute<TIntent>
    where TIntent : struct, Enum
{
    internal IntentRoute(ChoiceAnswer<TIntent> intent, ScoreAnswer? complexity, RouteTarget target, QuestionSetResult result)
    {
        IntentAnswer = intent;
        Complexity = complexity;
        Target = target;
        Result = result;
    }

    /// <summary>The most likely intent.</summary>
    public TIntent Intent => IntentAnswer.Choice;

    /// <summary>How concentrated the intent distribution is.</summary>
    public double Confidence => IntentAnswer.Confidence;

    /// <summary>The full intent answer.</summary>
    public ChoiceAnswer<TIntent> IntentAnswer { get; }

    /// <summary>The complexity answer, when a complexity scale was configured.</summary>
    public ScoreAnswer? Complexity { get; }

    /// <summary>Where to send the request.</summary>
    public RouteTarget Target { get; }

    /// <summary>All answers, including any extra questions.</summary>
    public QuestionSetResult Result { get; }
}

/// <summary>
/// The intent-routing pattern from the TypeSafe docs without any chat client: one choice question picks the intent,
/// an optional score question rates complexity, and thresholds decide between code, a model, or a person.
/// </summary>
/// <typeparam name="TIntent">The enum of intents; use <see cref="LabelAttribute"/> for labels and descriptions.</typeparam>
public sealed class TypeSafeIntentRouter<TIntent>
    where TIntent : struct, Enum
{
    private readonly ITypeSafeClient _client;
    private readonly ChoiceHandle<TIntent> _intent;
    private readonly ScoreHandle? _complexity;

    /// <summary>Creates a router.</summary>
    /// <param name="client">The TypeSafe client.</param>
    /// <param name="intentInstructions">The intent question, for example "What does the customer want?".</param>
    /// <param name="complexityLevels">Optional ordered complexity levels, simplest first. Omit to route on intent alone.</param>
    /// <param name="complexityInstructions">The complexity question; defaults to a generic one.</param>
    public TypeSafeIntentRouter(
        ITypeSafeClient client,
        TypeSafeContent intentInstructions,
        IEnumerable<string>? complexityLevels = null,
        TypeSafeContent? complexityInstructions = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        Questions = new QuestionSet();
        _intent = Questions.Choice<TIntent>(intentInstructions, id: "intent");
        if (complexityLevels is not null)
        {
            _complexity = Questions.Score(
                complexityInstructions ?? "How difficult is this request to resolve?",
                complexityLevels,
                id: "complexity");
        }
    }

    /// <summary>The questions sent. Add extra questions before routing to read them from <see cref="IntentRoute{TIntent}.Result"/>.</summary>
    public QuestionSet Questions { get; }

    /// <summary>Intent confidence below this routes to a person. Defaults to 0.5, as in the docs.</summary>
    public double ConfidenceFloor { get; set; } = 0.5;

    /// <summary>Intents that deterministic code handles when confident. Everything else goes to a model.</summary>
    public ISet<TIntent> CodeIntents { get; } = new HashSet<TIntent>();

    /// <summary>Complexity scores above this route to a person. Defaults to 1, as in the docs.</summary>
    public double ComplexityCeiling { get; set; } = 1;

    /// <summary>Complexity confidence below this routes to a person. Defaults to 0.5.</summary>
    public double ComplexityConfidenceFloor { get; set; } = 0.5;

    /// <summary>Judges the state and decides the route.</summary>
    public async Task<IntentRoute<TIntent>> RouteAsync(TypeSafeContent state, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await _client.SystemOneAsync(state, Questions, options, cancellationToken).ConfigureAwait(false);
        var intent = result.Get(_intent);
        var complexity = _complexity is null ? null : result.Get(_complexity);

        RouteTarget target;
        if (intent.Confidence < ConfidenceFloor)
        {
            target = RouteTarget.Human;
        }
        else if (complexity is not null && (complexity.Score > ComplexityCeiling || complexity.Confidence < ComplexityConfidenceFloor))
        {
            target = RouteTarget.Human;
        }
        else
        {
            target = CodeIntents.Contains(intent.Choice) ? RouteTarget.Code : RouteTarget.Model;
        }

        return new IntentRoute<TIntent>(intent, complexity, target, result);
    }
}
