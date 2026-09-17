using System.Diagnostics.CodeAnalysis;

namespace TypeSafeAI;

/// <summary>The answers to a <see cref="QuestionSet"/>, readable through the set's typed handles.</summary>
public sealed class QuestionSetResult
{
    private Dictionary<string, object>? _bound;

    internal QuestionSetResult(QuestionSet questions, SystemOneResponse response)
    {
        Questions = questions;
        Response = response;
    }

    /// <summary>The question set that produced this result.</summary>
    public QuestionSet Questions { get; }

    /// <summary>The raw response.</summary>
    public SystemOneResponse Response { get; }

    /// <summary>The model that produced the answers.</summary>
    public string Model => Response.Model;

    /// <summary>Token usage for the request.</summary>
    public Usage? Usage => Response.Usage;

    /// <summary>The <c>x-typesafe-request-id</c> header.</summary>
    public string? RequestId => Response.RequestId;

    /// <summary>Gets the untyped answer for a question id.</summary>
    /// <exception cref="KeyNotFoundException">No answer exists for the id.</exception>
    public Answer this[string questionId] => Response[questionId];

    /// <summary>Gets the typed answer for a handle.</summary>
    /// <exception cref="TypeSafeResponseValidationException">The response has no answer for the handle, or the answer does not match the question.</exception>
    public TResult Get<TResult>(IQuestionHandle<TResult> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (_bound is not null && _bound.TryGetValue(handle.Id, out var cached))
        {
            return (TResult)cached;
        }

        if (!Response.Answers.TryGetValue(handle.Id, out var answer))
        {
            throw new TypeSafeResponseValidationException($"The response has no answer for question '{handle.Id}'.") { RequestId = RequestId };
        }

        var bound = handle.Bind(answer);
        if (bound is not null)
        {
            (_bound ??= new Dictionary<string, object>(StringComparer.Ordinal))[handle.Id] = bound;
        }

        return bound;
    }

    /// <summary>Tries to get the typed answer for a handle without throwing.</summary>
    public bool TryGet<TResult>(IQuestionHandle<TResult> handle, [MaybeNullWhen(false)] out TResult result)
    {
        ArgumentNullException.ThrowIfNull(handle);
        try
        {
            result = Get(handle);
            return true;
        }
        catch (TypeSafeResponseValidationException)
        {
            result = default;
            return false;
        }
    }
}
