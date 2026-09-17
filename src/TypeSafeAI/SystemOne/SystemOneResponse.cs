using System.Text.Json.Serialization;

namespace TypeSafeAI;

/// <summary>The answers to a System One request.</summary>
public sealed class SystemOneResponse : TypeSafeResponse
{
    private IReadOnlyDictionary<string, NoulAnswer>? _nouls;
    private IReadOnlyDictionary<string, ChoiceAnswer>? _choices;
    private IReadOnlyDictionary<string, ScoreAnswer>? _scores;

    /// <summary>The model that produced the answers.</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>The answers keyed by the question ids from the request.</summary>
    [JsonPropertyName("answers")]
    public IReadOnlyDictionary<string, Answer> Answers { get; init; } = new Dictionary<string, Answer>(StringComparer.Ordinal);

    /// <summary>Token usage for the request.</summary>
    [JsonPropertyName("usage")]
    public Usage? Usage { get; init; }

    /// <summary>The noul answers only, keyed by question id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, NoulAnswer> Nouls => _nouls ??= Filter<NoulAnswer>();

    /// <summary>The choice answers only, keyed by question id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, ChoiceAnswer> Choices => _choices ??= Filter<ChoiceAnswer>();

    /// <summary>The score answers only, keyed by question id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, ScoreAnswer> Scores => _scores ??= Filter<ScoreAnswer>();

    /// <summary>Gets an answer by question id.</summary>
    /// <exception cref="KeyNotFoundException">No answer exists for the id.</exception>
    public Answer this[string questionId] => Answers.TryGetValue(questionId, out var answer)
        ? answer
        : throw new KeyNotFoundException($"The response has no answer for question '{questionId}'.");

    private Dictionary<string, T> Filter<T>() where T : Answer
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var pair in Answers)
        {
            if (pair.Value is T typed)
            {
                result.Add(pair.Key, typed);
            }
        }

        return result;
    }
}
