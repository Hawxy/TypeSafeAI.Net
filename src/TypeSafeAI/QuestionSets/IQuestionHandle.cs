namespace TypeSafeAI;

/// <summary>A question registered in a <see cref="QuestionSet"/>, identified by its id.</summary>
public interface IQuestionHandle
{
    /// <summary>The question id used as the key in the request and response.</summary>
    string Id { get; }

    /// <summary>True when the set generated the id rather than the caller supplying one.</summary>
    bool HasGeneratedId { get; }

    /// <summary>The question.</summary>
    Question Question { get; }
}

/// <summary>A question handle that knows how to turn its raw answer into a typed result.</summary>
/// <typeparam name="TResult">The typed answer.</typeparam>
public interface IQuestionHandle<out TResult> : IQuestionHandle
{
    /// <summary>Converts the raw answer into the typed result, validating that it matches the question.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The answer is of the wrong type or carries a label the question did not offer.</exception>
    TResult Bind(Answer answer);
}
