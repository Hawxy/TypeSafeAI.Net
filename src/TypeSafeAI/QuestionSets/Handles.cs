namespace TypeSafeAI;

/// <summary>Handle for a <see cref="NoulQuestion"/>.</summary>
public sealed class NoulHandle : IQuestionHandle<NoulAnswer>
{
    internal NoulHandle(string id, NoulQuestion question)
    {
        Id = id;
        Question = question;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The question.</summary>
    public NoulQuestion Question { get; }

    Question IQuestionHandle.Question => Question;

    /// <inheritdoc />
    public NoulAnswer Bind(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer as NoulAnswer ?? throw HandleErrors.WrongType(Id, "noul", answer);
    }
}

/// <summary>Handle for a <see cref="ChoiceQuestion"/> with string labels.</summary>
public sealed class ChoiceHandle : IQuestionHandle<ChoiceAnswer>
{
    internal ChoiceHandle(string id, ChoiceQuestion question)
    {
        Id = id;
        Question = question;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The question.</summary>
    public ChoiceQuestion Question { get; }

    Question IQuestionHandle.Question => Question;

    /// <summary>The labels offered, in declaration order.</summary>
    public IEnumerable<string> Labels => Question.Labels;

    /// <inheritdoc />
    public ChoiceAnswer Bind(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var choice = answer as ChoiceAnswer ?? throw HandleErrors.WrongType(Id, "choice", answer);
        if (!Question.Criteria.ContainsKey(choice.Choice))
        {
            throw HandleErrors.UnknownLabel(Id, choice.Choice);
        }

        return choice;
    }
}

/// <summary>Handle for a <see cref="ChoiceQuestion"/> whose labels map to an enum.</summary>
public sealed class ChoiceHandle<TEnum> : IQuestionHandle<ChoiceAnswer<TEnum>>
    where TEnum : struct, Enum
{
    internal ChoiceHandle(string id, ChoiceQuestion question)
    {
        Id = id;
        Question = question;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The question.</summary>
    public ChoiceQuestion Question { get; }

    Question IQuestionHandle.Question => Question;

    /// <inheritdoc />
    public ChoiceAnswer<TEnum> Bind(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var choice = answer as ChoiceAnswer ?? throw HandleErrors.WrongType(Id, "choice", answer);
        if (!EnumLabels<TEnum>.TryParse(choice.Choice, out var value))
        {
            throw HandleErrors.UnknownLabel(Id, choice.Choice);
        }

        var probabilities = new Dictionary<TEnum, double>();
        foreach (var pair in choice.Probabilities)
        {
            if (EnumLabels<TEnum>.TryParse(pair.Key, out var member))
            {
                probabilities[member] = pair.Value;
            }
        }

        return new ChoiceAnswer<TEnum>(value, choice.Choice, probabilities, choice.Confidence, choice);
    }
}

/// <summary>Handle for a <see cref="ScoreQuestion"/> with index-based levels.</summary>
public sealed class ScoreHandle : IQuestionHandle<ScoreAnswer>
{
    internal ScoreHandle(string id, ScoreQuestion question)
    {
        Id = id;
        Question = question;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The question.</summary>
    public ScoreQuestion Question { get; }

    Question IQuestionHandle.Question => Question;

    /// <summary>The number of levels on the scale.</summary>
    public int LevelCount => Question.LevelCount;

    /// <inheritdoc />
    public ScoreAnswer Bind(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer as ScoreAnswer ?? throw HandleErrors.WrongType(Id, "score", answer);
    }
}

/// <summary>Handle for a <see cref="ScoreQuestion"/> whose levels map to an enum's members in order.</summary>
public sealed class ScoreHandle<TEnum> : IQuestionHandle<ScoreAnswer<TEnum>>
    where TEnum : struct, Enum
{
    internal ScoreHandle(string id, ScoreQuestion question)
    {
        Id = id;
        Question = question;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The question.</summary>
    public ScoreQuestion Question { get; }

    Question IQuestionHandle.Question => Question;

    /// <inheritdoc />
    public ScoreAnswer<TEnum> Bind(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var score = answer as ScoreAnswer ?? throw HandleErrors.WrongType(Id, "score", answer);
        var members = EnumLabels<TEnum>.Members;

        var probabilities = new Dictionary<TEnum, double>();
        var legend = new Dictionary<TEnum, TypeSafeContent?>();
        foreach (var level in score.Levels)
        {
            if ((uint)level.Index < (uint)members.Count)
            {
                probabilities[members[level.Index]] = level.Probability;
                legend[members[level.Index]] = level.Description;
            }
        }

        var nearest = members[Math.Clamp(score.NearestLevel, 0, members.Count - 1)];
        var mostLikely = members[Math.Clamp(score.MostLikelyLevel, 0, members.Count - 1)];
        return new ScoreAnswer<TEnum>(score.Score, nearest, mostLikely, probabilities, legend, score.Confidence, score);
    }
}

internal static class HandleErrors
{
    public static TypeSafeResponseValidationException WrongType(string id, string expected, Answer answer) =>
        new($"Question '{id}' expected a {expected} answer but the API returned a {answer.Type} answer.");

    public static TypeSafeResponseValidationException UnknownLabel(string id, string label) =>
        new($"Question '{id}' received the label '{label}', which the question did not offer.");
}
