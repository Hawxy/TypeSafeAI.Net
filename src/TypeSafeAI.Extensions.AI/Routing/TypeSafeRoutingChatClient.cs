#pragma warning disable MEAI001 // RoutingChatClient and RoutingContext are experimental in Microsoft.Extensions.AI.

using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>The judged conversation handed to a routing selector.</summary>
public sealed class TypeSafeRoutingContext
{
    internal TypeSafeRoutingContext(QuestionSetResult result, RoutingContext routing, IChatClient defaultClient)
    {
        Result = result;
        Routing = routing;
        DefaultClient = defaultClient;
    }

    /// <summary>The TypeSafe answers.</summary>
    public QuestionSetResult Result { get; }

    /// <summary>The conversation being routed.</summary>
    public IEnumerable<ChatMessage> Messages => Routing.Messages;

    /// <summary>The chat options for this call. This is the clone the selected client receives, so changes here shape the request.</summary>
    public ChatOptions? Options => Routing.ChatOptions;

    /// <summary>The underlying Microsoft.Extensions.AI routing context. Experimental in Microsoft.Extensions.AI.</summary>
    public RoutingContext Routing { get; }

    /// <summary>The client used when the selector returns <see langword="null"/>.</summary>
    public IChatClient DefaultClient { get; }

    /// <summary>Gets a typed answer.</summary>
    public TResult Get<TResult>(IQuestionHandle<TResult> handle) => Result.Get(handle);
}

/// <summary>Configures <see cref="TypeSafeRoutingChatClient"/>.</summary>
public sealed class RoutingOptions
{
    /// <summary>Builds the state judged for routing. Defaults to <see cref="ChatState.FromMessages(IEnumerable{ChatMessage}, ChatResponse?)"/>.</summary>
    public Func<IReadOnlyList<ChatMessage>, TypeSafeContent>? StateBuilder { get; set; }

    /// <summary>Per-request overrides for the TypeSafe call.</summary>
    public RequestOptions? RequestOptions { get; set; }

    /// <summary>Attach the routing answers to <see cref="ChatResponse.AdditionalProperties"/> of non-streaming responses. On by default.</summary>
    public bool AnnotateResponses { get; set; } = true;
}

/// <summary>
/// A <see cref="RoutingChatClient"/> that judges each conversation with TypeSafe and hands it to the client a selector picks,
/// falling back to a default client. The TypeSafe counterpart of <c>SemanticRoutingChatClient</c>: it implements the
/// intent-routing and confidence-gated routing patterns from the TypeSafe docs, and composes with the framework's
/// failover clients.
/// </summary>
public sealed class TypeSafeRoutingChatClient : RoutingChatClient
{
    /// <summary>Key under which the routing <see cref="QuestionSetResult"/> is attached to <see cref="ChatResponse.AdditionalProperties"/>.</summary>
    public const string ResultPropertyName = "typesafe.routing";

    private readonly ITypeSafeClient _typeSafe;
    private readonly QuestionSet _questions;
    private readonly Func<TypeSafeRoutingContext, IChatClient?> _select;
    private readonly IChatClient _defaultClient;
    private readonly RoutingOptions _options;

    /// <summary>Creates the client. Selected clients are caller-owned and are not disposed with this instance.</summary>
    /// <param name="typeSafeClient">The TypeSafe client.</param>
    /// <param name="questions">The questions asked of each conversation.</param>
    /// <param name="select">Picks a client from the answers; return <see langword="null"/> for <paramref name="defaultClient"/>.</param>
    /// <param name="defaultClient">The client used when the selector returns <see langword="null"/>.</param>
    /// <param name="options">Optional settings.</param>
    public TypeSafeRoutingChatClient(
        ITypeSafeClient typeSafeClient,
        QuestionSet questions,
        Func<TypeSafeRoutingContext, IChatClient?> select,
        IChatClient defaultClient,
        RoutingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(typeSafeClient);
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(defaultClient);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one routing question is required.", nameof(questions));
        }

        _typeSafe = typeSafeClient;
        _questions = questions;
        _select = select;
        _defaultClient = defaultClient;
        _options = options ?? new RoutingOptions();
    }

    /// <inheritdoc />
    protected override async ValueTask<IChatClient> SelectClientAsync(RoutingContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var conversation = context.Messages as IReadOnlyList<ChatMessage> ?? context.Messages.ToList();
        var state = _options.StateBuilder?.Invoke(conversation) ?? ChatState.FromMessages(conversation);
        var result = await _typeSafe.SystemOneAsync(state, _questions, _options.RequestOptions, cancellationToken).ConfigureAwait(false);
        var target = _select(new TypeSafeRoutingContext(result, context, _defaultClient)) ?? _defaultClient;

        // Per the RoutingChatClient contract, request-specific behaviour is attached to the returned client.
        return _options.AnnotateResponses ? new AnnotatingChatClient(target, result) : target;
    }

    // Attaches the routing answers to the response without owning the wrapped client.
    private sealed class AnnotatingChatClient(IChatClient inner, QuestionSetResult result) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            response.AdditionalProperties ??= [];
            response.AdditionalProperties[ResultPropertyName] = result;
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            // The selected client is caller-owned.
        }
    }
}
