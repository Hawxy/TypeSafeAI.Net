namespace TypeSafeAI;

/// <summary>Convenience overloads for <see cref="ITypeSafeClient"/>.</summary>
public static class TypeSafeClientExtensions
{
    /// <summary>Evaluates a state against questions keyed by id.</summary>
    public static Task<SystemOneResponse> SystemOneAsync(
        this ITypeSafeClient client,
        TypeSafeContent state,
        IReadOnlyDictionary<string, Question> questions,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.SystemOneAsync(new SystemOneRequest { State = state, Questions = questions }, options, cancellationToken);
    }

    /// <summary>Evaluates a state against a <see cref="QuestionSet"/> and returns typed access to the answers.</summary>
    public static async Task<QuestionSetResult> SystemOneAsync(
        this ITypeSafeClient client,
        TypeSafeContent state,
        QuestionSet questions,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(questions);
        var response = await client.SystemOneAsync(new SystemOneRequest { State = state, Questions = questions }, options, cancellationToken).ConfigureAwait(false);
        return new QuestionSetResult(questions, response);
    }

    /// <summary>
    /// Evaluates many states against the same <see cref="QuestionSet"/>, one request per state, in parallel.
    /// Results are returned in input order. A failure for any state fails the whole call.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="states">The states to judge.</param>
    /// <param name="questions">The questions asked of every state.</param>
    /// <param name="maxConcurrency">How many requests may be in flight at once.</param>
    /// <param name="options">Per-request overrides applied to every request.</param>
    /// <param name="cancellationToken">Cancels all requests.</param>
    public static async Task<IReadOnlyList<QuestionSetResult>> SystemOneManyAsync(
        this ITypeSafeClient client,
        IEnumerable<TypeSafeContent> states,
        QuestionSet questions,
        int maxConcurrency = 8,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        var items = states as IReadOnlyList<TypeSafeContent> ?? states.ToArray();
        if (items.Count == 0)
        {
            return [];
        }

        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new Task<QuestionSetResult>[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            tasks[i] = RunAsync(items[i]);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);

        async Task<QuestionSetResult> RunAsync(TypeSafeContent state)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await client.SystemOneAsync(state, questions, options, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
