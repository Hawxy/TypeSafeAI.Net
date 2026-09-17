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
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var conversation = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var outcomes = new List<GuardrailOutcome>();

        var inputOutcome = await GuardInputAsync(conversation, cancellationToken).ConfigureAwait(false);
        if (inputOutcome is not null)
        {
            outcomes.Add(inputOutcome);
            if (inputOutcome.Action == GuardrailAction.Block)
            {
                return Blocked(inputOutcome, outcomes);
            }
        }

        var response = await base.GetResponseAsync(conversation, options, cancellationToken).ConfigureAwait(false);

        var outputOutcome = await GuardOutputAsync(conversation, response, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        var inputOutcome = await GuardInputAsync(conversation, cancellationToken).ConfigureAwait(false);
        if (inputOutcome?.Action == GuardrailAction.Block)
        {
            foreach (var update in Blocked(inputOutcome, [inputOutcome]).ToChatResponseUpdates())
            {
                yield return update;
            }

            yield break;
        }

        if (_options.OutputQuestions is null || !_options.GuardStreamingOutput)
        {
            await foreach (var update in base.GetStreamingResponseAsync(conversation, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        // Buffer the whole response so the output guard can judge it before anything reaches the caller.
        var response = await base.GetStreamingResponseAsync(conversation, options, cancellationToken).ToChatResponseAsync(cancellationToken).ConfigureAwait(false);
        var outcomes = new List<GuardrailOutcome>();
        if (inputOutcome is not null)
        {
            outcomes.Add(inputOutcome);
        }

        var outputOutcome = await GuardOutputAsync(conversation, response, cancellationToken).ConfigureAwait(false);
        if (outputOutcome is not null)
        {
            outcomes.Add(outputOutcome);
            if (outputOutcome.Action == GuardrailAction.Block)
            {
                response = Blocked(outputOutcome, outcomes);
            }
        }

        if (outputOutcome?.Action != GuardrailAction.Block)
        {
            Annotate(response, outcomes);
        }

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    private async Task<GuardrailOutcome?> GuardInputAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken)
    {
        if (_options.InputQuestions is null)
        {
            return null;
        }

        var state = _options.InputStateBuilder?.Invoke(conversation) ?? ChatState.FromMessages(conversation);
        var result = await _typeSafe.SystemOneAsync(state, _options.InputQuestions, _options.RequestOptions, cancellationToken).ConfigureAwait(false);
        var decision = _options.PolicyFor(GuardrailDirection.Input)(new GuardrailAssessment(GuardrailDirection.Input, result, conversation, null));
        return new GuardrailOutcome(GuardrailDirection.Input, decision.Action, decision.Reason, result.RequestId);
    }

    private async Task<GuardrailOutcome?> GuardOutputAsync(IReadOnlyList<ChatMessage> conversation, ChatResponse response, CancellationToken cancellationToken)
    {
        if (_options.OutputQuestions is null)
        {
            return null;
        }

        var state = _options.OutputStateBuilder?.Invoke(conversation, response) ?? ChatState.FromMessages(conversation, response);
        var result = await _typeSafe.SystemOneAsync(state, _options.OutputQuestions, _options.RequestOptions, cancellationToken).ConfigureAwait(false);
        var decision = _options.PolicyFor(GuardrailDirection.Output)(new GuardrailAssessment(GuardrailDirection.Output, result, conversation, response));
        return new GuardrailOutcome(GuardrailDirection.Output, decision.Action, decision.Reason, result.RequestId);
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
