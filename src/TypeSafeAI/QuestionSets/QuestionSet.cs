using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace TypeSafeAI;

/// <summary>
/// A set of questions with typed handles. Register questions, send the set with the state, then read answers through the handles.
/// Ids are for your code only and are generated (<c>q0</c>, <c>q1</c>, ...) unless you supply one.
/// </summary>
/// <example>
/// <code>
/// var q = new QuestionSet();
/// var category = q.Choice&lt;TicketCategory&gt;("What is this ticket about?");
/// var urgent = q.Noul("Does this convey urgency?");
/// var result = await client.SystemOneAsync(ticketText, q);
/// TicketCategory c = result.Get(category).Choice;
/// double p = result.Get(urgent).Probability;
/// </code>
/// </example>
public sealed class QuestionSet : IReadOnlyDictionary<string, Question>
{
    private readonly Dictionary<string, Question> _questions = new(StringComparer.Ordinal);
    private readonly List<IQuestionHandle> _handles = [];
    private int _nextId;

    /// <summary>The handles in registration order.</summary>
    public IReadOnlyList<IQuestionHandle> Handles => _handles;

    /// <inheritdoc />
    public int Count => _questions.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _handles.Select(h => h.Id);

    /// <inheritdoc />
    public IEnumerable<Question> Values => _handles.Select(h => h.Question);

    /// <inheritdoc />
    public Question this[string id] => _questions[id];

    /// <summary>Adds a yes/no question.</summary>
    public NoulHandle Noul(TypeSafeContent? instructions, TypeSafeContent? yes = null, TypeSafeContent? no = null, string? id = null) =>
        Add(id, Question.Noul(instructions, yes, no));

    /// <summary>Adds a pick-one question over plain labels.</summary>
    public ChoiceHandle Choice(TypeSafeContent? instructions, params string[] labels) =>
        Add(null, Question.Choice(instructions, labels));

    /// <summary>Adds a pick-one question over plain labels with an optional id.</summary>
    public ChoiceHandle Choice(TypeSafeContent? instructions, IEnumerable<string> labels, string? id = null) =>
        Add(id, Question.Choice(instructions, labels));

    /// <summary>Adds a pick-one question over labels with descriptions.</summary>
    public ChoiceHandle Choice(TypeSafeContent? instructions, IReadOnlyDictionary<string, TypeSafeContent?> criteria, string? id = null) =>
        Add(id, Question.Choice(instructions, criteria));

    /// <summary>Adds a pick-one question whose labels come from an enum.</summary>
    public ChoiceHandle<TEnum> Choice<TEnum>(TypeSafeContent? instructions, string? id = null)
        where TEnum : struct, Enum =>
        Add<TEnum>(id, Question.Choice<TEnum>(instructions));

    /// <summary>Adds a pick-one question whose labels come from an enum, with descriptions supplied per member.</summary>
    public ChoiceHandle<TEnum> Choice<TEnum>(TypeSafeContent? instructions, IReadOnlyDictionary<TEnum, TypeSafeContent?> criteria, string? id = null)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var wire = new Dictionary<string, TypeSafeContent?>(StringComparer.Ordinal);
        foreach (var pair in criteria)
        {
            wire[EnumLabels<TEnum>.GetLabel(pair.Key)] = pair.Value;
        }

        return Add<TEnum>(id, Question.Choice(instructions, wire));
    }

    /// <summary>Adds a rating question over ordered levels.</summary>
    public ScoreHandle Score(TypeSafeContent? instructions, params TypeSafeContent?[] levels) =>
        Add(null, Question.Score(instructions, levels));

    /// <summary>Adds a rating question over ordered text levels.</summary>
    public ScoreHandle Score(TypeSafeContent? instructions, IEnumerable<string> levels, string? id = null) =>
        Add(id, Question.Score(instructions, levels));

    /// <summary>Adds a rating question whose levels are an enum's members in order.</summary>
    public ScoreHandle<TEnum> Score<TEnum>(TypeSafeContent? instructions, string? id = null)
        where TEnum : struct, Enum =>
        Add<TEnum>(id, Question.Score<TEnum>(instructions));

    /// <summary>Adds a prepared noul question.</summary>
    public NoulHandle Add(string? id, NoulQuestion question) => Register(new NoulHandle(Reserve(id), Check(question)));

    /// <summary>Adds a prepared choice question.</summary>
    public ChoiceHandle Add(string? id, ChoiceQuestion question) => Register(new ChoiceHandle(Reserve(id), Check(question)));

    /// <summary>Adds a prepared choice question whose labels map to an enum.</summary>
    public ChoiceHandle<TEnum> Add<TEnum>(string? id, ChoiceQuestion question)
        where TEnum : struct, Enum
    {
        Check(question);
        foreach (var label in EnumLabels<TEnum>.Labels)
        {
            if (!question.Criteria.ContainsKey(label))
            {
                throw new ArgumentException($"The question does not offer the label '{label}' declared by {typeof(TEnum).Name}.", nameof(question));
            }
        }

        return Register(new ChoiceHandle<TEnum>(Reserve(id), question));
    }

    /// <summary>Adds a prepared score question.</summary>
    public ScoreHandle Add(string? id, ScoreQuestion question) => Register(new ScoreHandle(Reserve(id), Check(question)));

    /// <summary>Adds a prepared score question whose levels map to an enum's members in order.</summary>
    public ScoreHandle<TEnum> Add<TEnum>(string? id, ScoreQuestion question)
        where TEnum : struct, Enum
    {
        Check(question);
        if (question.LevelCount != EnumLabels<TEnum>.Members.Count)
        {
            throw new ArgumentException(
                $"The question has {question.LevelCount} levels but {typeof(TEnum).Name} declares {EnumLabels<TEnum>.Members.Count} members.",
                nameof(question));
        }

        return Register(new ScoreHandle<TEnum>(Reserve(id), question));
    }

    /// <summary>Adds any question without a typed handle. Read its answer through <see cref="QuestionSetResult.this[string]"/>.</summary>
    public IQuestionHandle Add(string id, Question question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Check(question) switch
        {
            NoulQuestion noul => Add(id, noul),
            ChoiceQuestion choice => Add(id, choice),
            ScoreQuestion score => Add(id, score),
            _ => throw new ArgumentException($"Unsupported question type {question.GetType().Name}.", nameof(question)),
        };
    }

    /// <inheritdoc />
    public bool ContainsKey(string key) => _questions.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out Question value) => _questions.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, Question>> GetEnumerator() =>
        _handles.Select(h => new KeyValuePair<string, Question>(h.Id, h.Question)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private THandle Register<THandle>(THandle handle)
        where THandle : IQuestionHandle
    {
        _questions.Add(handle.Id, handle.Question);
        _handles.Add(handle);
        return handle;
    }

    private string Reserve(string? id)
    {
        if (id is not null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Question ids must be non-empty.", nameof(id));
            }

            if (_questions.ContainsKey(id))
            {
                throw new ArgumentException($"A question with id '{id}' already exists.", nameof(id));
            }

            return id;
        }

        string generated;
        do
        {
            generated = "q" + _nextId++;
        }
        while (_questions.ContainsKey(generated));

        return generated;
    }

    private static TQuestion Check<TQuestion>(TQuestion question)
        where TQuestion : Question
    {
        ArgumentNullException.ThrowIfNull(question);
        return question;
    }
}
