namespace TypeSafeAI;

/// <summary>
/// Base for typed question handles: carries the id and question, checks that an answer is of the matching kind,
/// then projects it to the handle's result type.
/// </summary>
/// <typeparam name="TQuestion">The question type.</typeparam>
/// <typeparam name="TAnswer">The wire answer type that matches the question.</typeparam>
/// <typeparam name="TResult">The typed result.</typeparam>
public abstract class QuestionHandle<TQuestion, TAnswer, TResult> : IQuestionHandle<TResult>
    where TQuestion : Question
    where TAnswer : Answer
{
    private protected QuestionHandle(ReservedId id, TQuestion question)
    {
        Id = id.Value;
        HasGeneratedId = id.Generated;
        Question = question;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public bool HasGeneratedId { get; }

    /// <summary>The question.</summary>
    public TQuestion Question { get; }

    Question IQuestionHandle.Question => Question;

    /// <inheritdoc />
    public TResult Bind(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var typed = answer as TAnswer
            ?? throw new TypeSafeResponseValidationException($"Question '{Id}' expected a {Question.Type} answer but the API returned a {answer.Type} answer.");
        return Project(typed);
    }

    private protected abstract TResult Project(TAnswer answer);

    private protected TypeSafeResponseValidationException UnknownLabel(string label) =>
        new($"Question '{Id}' received the label '{label}', which the question did not offer.");
}

/// <summary>Handle for a <see cref="NoulQuestion"/>.</summary>
public sealed class NoulHandle : QuestionHandle<NoulQuestion, NoulAnswer, NoulAnswer>
{
    internal NoulHandle(ReservedId id, NoulQuestion question)
        : base(id, question)
    {
    }

    private protected override NoulAnswer Project(NoulAnswer answer) => answer;
}

/// <summary>Handle for a <see cref="ChoiceQuestion"/> with string labels.</summary>
public sealed class ChoiceHandle : QuestionHandle<ChoiceQuestion, ChoiceAnswer, ChoiceAnswer>
{
    internal ChoiceHandle(ReservedId id, ChoiceQuestion question)
        : base(id, question)
    {
    }

    /// <summary>The labels offered, in declaration order.</summary>
    public IEnumerable<string> Labels => Question.Labels;

    private protected override ChoiceAnswer Project(ChoiceAnswer answer) =>
        Question.Criteria.ContainsKey(answer.Choice) ? answer : throw UnknownLabel(answer.Choice);
}

/// <summary>Handle for a <see cref="ChoiceQuestion"/> whose labels map to an enum.</summary>
public sealed class ChoiceHandle<TEnum> : QuestionHandle<ChoiceQuestion, ChoiceAnswer, ChoiceAnswer<TEnum>>
    where TEnum : struct, Enum
{
    internal ChoiceHandle(ReservedId id, ChoiceQuestion question)
        : base(id, question)
    {
    }

    private protected override ChoiceAnswer<TEnum> Project(ChoiceAnswer answer)
    {
        if (!EnumLabels<TEnum>.TryParse(answer.Choice, out var value))
        {
            throw UnknownLabel(answer.Choice);
        }

        var probabilities = new Dictionary<TEnum, double>(answer.Probabilities.Count);
        foreach (var pair in answer.Probabilities)
        {
            if (EnumLabels<TEnum>.TryParse(pair.Key, out var member))
            {
                probabilities[member] = pair.Value;
            }
        }

        return new ChoiceAnswer<TEnum>(value, probabilities, answer);
    }
}

/// <summary>Handle for a <see cref="ScoreQuestion"/> with index-based levels.</summary>
public sealed class ScoreHandle : QuestionHandle<ScoreQuestion, ScoreAnswer, ScoreAnswer>
{
    internal ScoreHandle(ReservedId id, ScoreQuestion question)
        : base(id, question)
    {
    }

    /// <summary>The number of levels on the scale.</summary>
    public int LevelCount => Question.LevelCount;

    private protected override ScoreAnswer Project(ScoreAnswer answer) => answer;
}

/// <summary>Handle for a <see cref="ScoreQuestion"/> whose levels map to an enum's members in order.</summary>
public sealed class ScoreHandle<TEnum> : QuestionHandle<ScoreQuestion, ScoreAnswer, ScoreAnswer<TEnum>>
    where TEnum : struct, Enum
{
    internal ScoreHandle(ReservedId id, ScoreQuestion question)
        : base(id, question)
    {
    }

    private protected override ScoreAnswer<TEnum> Project(ScoreAnswer answer)
    {
        var members = EnumLabels<TEnum>.Members;
        var probabilities = new Dictionary<TEnum, double>(members.Count);
        var legend = new Dictionary<TEnum, TypeSafeContent?>(members.Count);
        foreach (var level in answer.Levels)
        {
            if ((uint)level.Index < (uint)members.Count)
            {
                probabilities[members[level.Index]] = level.Probability;
                legend[members[level.Index]] = level.Description;
            }
        }

        var nearest = members[Math.Clamp(answer.NearestLevel, 0, members.Count - 1)];
        var mostLikely = members[Math.Clamp(answer.MostLikelyLevel, 0, members.Count - 1)];
        return new ScoreAnswer<TEnum>(nearest, mostLikely, probabilities, legend, answer);
    }
}

/// <summary>A question id reserved in a <see cref="QuestionSet"/>, recording whether the set generated it.</summary>
public readonly record struct ReservedId(string Value, bool Generated);
