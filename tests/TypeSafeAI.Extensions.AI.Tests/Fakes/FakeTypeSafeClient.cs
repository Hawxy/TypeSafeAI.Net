namespace TypeSafeAI.Extensions.AI.Tests.Fakes;

/// <summary>An in-memory TypeSafe client that answers from a script and records requests.</summary>
public sealed class FakeTypeSafeClient : ITypeSafeClient
{
    private readonly Func<SystemOneRequest, SystemOneResponse> _respond;

    public FakeTypeSafeClient(Func<SystemOneRequest, SystemOneResponse> respond)
    {
        _respond = respond;
    }

    public List<SystemOneRequest> Requests { get; } = [];

    public List<RequestOptions?> Options { get; } = [];

    public IModelsResource Models => throw new NotSupportedException();

    public Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        Options.Add(options);
        return Task.FromResult(_respond(request));
    }

    /// <summary>Answers every question with the supplied answers, keyed by question id.</summary>
    public static FakeTypeSafeClient Answering(params (string Id, Answer Answer)[] answers) =>
        new(_ => Response(answers));

    public static SystemOneResponse Response(params (string Id, Answer Answer)[] answers) => new()
    {
        Model = "jev-test",
        Answers = answers.ToDictionary(a => a.Id, a => a.Answer, StringComparer.Ordinal),
        Usage = new Usage { InputTokens = 100, OutputTokens = 5 },
    };

    public static NoulAnswer Noul(double p) => new() { Probability = p };

    public static ChoiceAnswer Choice(string choice, double confidence, params (string Label, double P)[] probabilities) => new()
    {
        Choice = choice,
        Confidence = confidence,
        Probabilities = probabilities.ToDictionary(p => p.Label, p => p.P, StringComparer.Ordinal),
    };

    public static ScoreAnswer Score(double score, double confidence, params double[] probabilities) => new()
    {
        Score = score,
        Confidence = confidence,
        Probabilities = probabilities.Select((p, i) => (i, p)).ToDictionary(x => x.i, x => x.p),
        Legend = probabilities.Select((_, i) => i).ToDictionary(i => i, i => (TypeSafeContent?)TypeSafeContent.FromText("level " + i)),
    };
}
