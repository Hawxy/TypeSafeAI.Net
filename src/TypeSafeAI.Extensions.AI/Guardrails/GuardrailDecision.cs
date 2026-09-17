using Microsoft.Extensions.AI;

namespace TypeSafeAI.Extensions.AI;

/// <summary>Which side of the LLM call a guardrail judged.</summary>
public enum GuardrailDirection
{
    /// <summary>The user's request, before it reaches the model.</summary>
    Input,

    /// <summary>The model's response, before it reaches the user.</summary>
    Output,
}

/// <summary>What a guardrail does with the message it judged.</summary>
public enum GuardrailAction
{
    /// <summary>Let it through unchanged.</summary>
    Allow,

    /// <summary>Let it through but flag it for review in the response's additional properties.</summary>
    Review,

    /// <summary>Stop it: return the configured refusal, or throw when <see cref="GuardrailOptions.ThrowWhenBlocked"/> is set.</summary>
    Block,
}

/// <summary>The outcome of a guardrail policy.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Reason">Why, for logs and review queues.</param>
public sealed record GuardrailDecision(GuardrailAction Action, string? Reason = null)
{
    /// <summary>Let the message through.</summary>
    public static GuardrailDecision Allow { get; } = new(GuardrailAction.Allow);

    /// <summary>Let the message through but flag it.</summary>
    public static GuardrailDecision Review(string reason) => new(GuardrailAction.Review, reason);

    /// <summary>Stop the message.</summary>
    public static GuardrailDecision Block(string reason) => new(GuardrailAction.Block, reason);
}

/// <summary>What the guardrail saw and decided, attached to responses under <see cref="TypeSafeGuardrailChatClient.OutcomePropertyName"/>.</summary>
/// <param name="Direction">Which side was judged.</param>
/// <param name="Action">What was done.</param>
/// <param name="Reason">Why.</param>
/// <param name="RequestId">The TypeSafe request id for the judgment.</param>
public sealed record GuardrailOutcome(GuardrailDirection Direction, GuardrailAction Action, string? Reason, string? RequestId);

/// <summary>The judged state handed to a guardrail policy.</summary>
public sealed class GuardrailAssessment
{
    internal GuardrailAssessment(GuardrailDirection direction, QuestionSetResult result, IReadOnlyList<ChatMessage> messages, ChatResponse? response)
    {
        Direction = direction;
        Result = result;
        Messages = messages;
        Response = response;
    }

    /// <summary>Which side was judged.</summary>
    public GuardrailDirection Direction { get; }

    /// <summary>The TypeSafe answers.</summary>
    public QuestionSetResult Result { get; }

    /// <summary>The conversation that was judged.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>The model response, for output guards.</summary>
    public ChatResponse? Response { get; }

    /// <summary>Gets a typed answer.</summary>
    public TResult Get<TResult>(IQuestionHandle<TResult> handle) => Result.Get(handle);
}

/// <summary>Thrown when a guardrail blocks a message and <see cref="GuardrailOptions.ThrowWhenBlocked"/> is set.</summary>
public sealed class TypeSafeGuardrailException : TypeSafeException
{
    /// <summary>Creates the exception.</summary>
    public TypeSafeGuardrailException(GuardrailOutcome outcome)
        : base($"The {outcome.Direction.ToString().ToLowerInvariant()} guardrail blocked the message: {outcome.Reason ?? "no reason given"}")
    {
        Outcome = outcome;
        RequestId = outcome.RequestId;
    }

    /// <summary>The blocking outcome.</summary>
    public GuardrailOutcome Outcome { get; }
}
