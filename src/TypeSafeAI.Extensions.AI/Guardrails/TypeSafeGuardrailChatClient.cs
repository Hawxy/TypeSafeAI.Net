using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>
/// Screens conversations with TypeSafe judgments before they reach the inner chat client and, optionally, screens the
/// response before it reaches the caller. Implements the guardrails pattern from the TypeSafe docs as chat-client middleware.
/// </summary>
public sealed class TypeSafeGuardrailChatClient : DelegatingChatClient
{
    /// <summary>Key under which <see cref="GuardrailOutcome"/>s are attached to <see cref="ChatResponse.AdditionalProperties"/>.</summary>
    public const string OutcomePropertyName = "typesafe.guardrail";

    private readonly ITypeSafeClient _typeSafe;
    private readonly GuardrailOptions _options;

    /// <summary>Creates the client.</summary>
    public TypeSafeGuardrailChatClient(IChatClient innerClient, ITypeSafeClient typeSafeClient, GuardrailOptions options)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(typeSafeClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _typeSafe = typeSafeClient;
        _options = options;
    }

    /// <inheritdoc />
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var conversation = messages.AsReadOnlyList();
        return GuardedAsync(conversation, () => base.GetResponseAsync(conversation, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = messages.AsReadOnlyList();

        if (_options.OutputQuestions is not null && _options.GuardStreamingOutput)
        {
            // Buffer the whole response so the output guard can judge it before anything reaches the caller.
            var guarded = await GuardedAsync(
                conversation,
                () => base.GetStreamingResponseAsync(conversation, options, cancellationToken).ToChatResponseAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

            foreach (var update in guarded.ToChatResponseUpdates())
            {
                yield return update;
            }

            yield break;
        }

        var inputOutcome = await GuardAsync(GuardrailDirection.Input, conversation, null, cancellationToken).ConfigureAwait(false);
        if (inputOutcome?.Action == GuardrailAction.Block)
        {
            foreach (var update in Blocked(inputOutcome, [inputOutcome]).ToChatResponseUpdates())
            {
                yield return update;
            }

            yield break;
        }

        await foreach (var update in base.GetStreamingResponseAsync(conversation, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    // Input guard, inner call, output guard: the sequence both the non-streaming and the buffered streaming path follow.
    private async Task<ChatResponse> GuardedAsync(IReadOnlyList<ChatMessage> conversation, Func<Task<ChatResponse>> inner, CancellationToken cancellationToken)
    {
        var outcomes = new List<GuardrailOutcome>(2);

        var inputOutcome = await GuardAsync(GuardrailDirection.Input, conversation, null, cancellationToken).ConfigureAwait(false);
        if (inputOutcome is not null)
        {
            outcomes.Add(inputOutcome);
            if (inputOutcome.Action == GuardrailAction.Block)
            {
                return Blocked(inputOutcome, outcomes);
            }
        }

        var response = await inner().ConfigureAwait(false);

        var outputOutcome = await GuardAsync(GuardrailDirection.Output, conversation, response, cancellationToken).ConfigureAwait(false);
        if (outputOutcome is not null)
        {
            outcomes.Add(outputOutcome);
            if (outputOutcome.Action == GuardrailAction.Block)
            {
                return Blocked(outputOutcome, outcomes);
            }
        }

        Annotate(response, outcomes);
        return response;
    }

    private async Task<GuardrailOutcome?> GuardAsync(GuardrailDirection direction, IReadOnlyList<ChatMessage> conversation, ChatResponse? response, CancellationToken cancellationToken)
    {
        var input = direction == GuardrailDirection.Input;
        var questions = input ? _options.InputQuestions : _options.OutputQuestions;
        if (questions is null)
        {
            return null;
        }

        var state = input
            ? _options.InputStateBuilder?.Invoke(conversation) ?? ChatState.FromMessages(conversation)
            : _options.OutputStateBuilder?.Invoke(conversation, response!) ?? ChatState.FromMessages(conversation, response);
        var result = await _typeSafe.SystemOneAsync(state, questions, _options.RequestOptions, cancellationToken).ConfigureAwait(false);
        var decision = _options.PolicyFor(direction)(new GuardrailAssessment(direction, result, conversation, response));
        return new GuardrailOutcome(direction, decision.Action, decision.Reason, result.RequestId);
    }

    private ChatResponse Blocked(GuardrailOutcome outcome, IReadOnlyList<GuardrailOutcome> outcomes)
    {
        if (_options.ThrowWhenBlocked)
        {
            throw new TypeSafeGuardrailException(outcome);
        }

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, _options.BlockedMessage))
        {
            FinishReason = ChatFinishReason.ContentFilter,
        };
        Annotate(response, outcomes);
        return response;
    }

    private static void Annotate(ChatResponse response, IReadOnlyList<GuardrailOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        response.AdditionalProperties ??= [];
        response.AdditionalProperties[OutcomePropertyName] = outcomes;
    }
}
