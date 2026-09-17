using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>Configures <see cref="TypeSafeGuardrailChatClient"/>.</summary>
public sealed class GuardrailOptions
{
    /// <summary>Questions asked about the conversation before it reaches the model. Leave null to skip the input guard.</summary>
    public QuestionSet? InputQuestions { get; set; }

    /// <summary>Questions asked about the model response before it reaches the user. Leave null to skip the output guard.</summary>
    public QuestionSet? OutputQuestions { get; set; }

    /// <summary>Turns the input assessment into a decision. Falls back to <see cref="Decide"/>.</summary>
    public Func<GuardrailAssessment, GuardrailDecision>? DecideInput { get; set; }

    /// <summary>Turns the output assessment into a decision. Falls back to <see cref="Decide"/>.</summary>
    public Func<GuardrailAssessment, GuardrailDecision>? DecideOutput { get; set; }

    /// <summary>Turns either assessment into a decision when no direction-specific policy is set. See <see cref="GuardrailPolicies"/>.</summary>
    public Func<GuardrailAssessment, GuardrailDecision>? Decide { get; set; }

    /// <summary>The assistant text returned in place of a blocked message.</summary>
    public string BlockedMessage { get; set; } = "I can't help with that request.";

    /// <summary>Throw <see cref="TypeSafeGuardrailException"/> instead of returning <see cref="BlockedMessage"/>.</summary>
    public bool ThrowWhenBlocked { get; set; }

    /// <summary>
    /// Buffer streaming responses so the output guard can judge them. Off by default because it removes streaming's latency benefit;
    /// when off, streaming responses skip the output guard.
    /// </summary>
    public bool GuardStreamingOutput { get; set; }

    /// <summary>Builds the input state. Defaults to <see cref="ChatState.FromMessages(IEnumerable{ChatMessage}, ChatResponse?)"/>.</summary>
    public Func<IReadOnlyList<ChatMessage>, TypeSafeContent>? InputStateBuilder { get; set; }

    /// <summary>Builds the output state. Defaults to <see cref="ChatState.FromMessages(IEnumerable{ChatMessage}, ChatResponse?)"/> with the response.</summary>
    public Func<IReadOnlyList<ChatMessage>, ChatResponse, TypeSafeContent>? OutputStateBuilder { get; set; }

    /// <summary>Per-request overrides for the TypeSafe calls.</summary>
    public RequestOptions? RequestOptions { get; set; }

    internal Func<GuardrailAssessment, GuardrailDecision> PolicyFor(GuardrailDirection direction) =>
        (direction == GuardrailDirection.Input ? DecideInput : DecideOutput)
        ?? Decide
        ?? throw new InvalidOperationException($"No guardrail policy is configured for {direction}. Set {nameof(Decide)} or the direction-specific policy.");

    internal void Validate()
    {
        if (InputQuestions is null && OutputQuestions is null)
        {
            throw new InvalidOperationException($"Set {nameof(InputQuestions)}, {nameof(OutputQuestions)}, or both.");
        }

        if (InputQuestions is not null)
        {
            _ = PolicyFor(GuardrailDirection.Input);
        }

        if (OutputQuestions is not null)
        {
            _ = PolicyFor(GuardrailDirection.Output);
        }
    }
}
