using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TypeSafeAI;
using TypeSafeAI.Extensions.AI;

namespace Microsoft.Extensions.AI;

/// <summary>Adds TypeSafe middleware to a <see cref="ChatClientBuilder"/> pipeline.</summary>
public static class TypeSafeChatClientBuilderExtensions
{
    /// <summary>Screens conversations and responses with TypeSafe judgments. See <see cref="TypeSafeGuardrailChatClient"/>.</summary>
    public static ChatClientBuilder UseTypeSafeGuardrail(this ChatClientBuilder builder, ITypeSafeClient typeSafeClient, Action<GuardrailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(typeSafeClient);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GuardrailOptions();
        configure(options);
        return builder.Use(inner => new TypeSafeGuardrailChatClient(inner, typeSafeClient, options));
    }

    /// <summary>Screens conversations and responses with TypeSafe judgments, resolving <see cref="ITypeSafeClient"/> from services.</summary>
    public static ChatClientBuilder UseTypeSafeGuardrail(this ChatClientBuilder builder, Action<GuardrailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GuardrailOptions();
        configure(options);
        return builder.Use((inner, services) => new TypeSafeGuardrailChatClient(inner, services.GetRequiredService<ITypeSafeClient>(), options));
    }

    /// <summary>
    /// Routes each conversation to the client a selector picks from TypeSafe answers, with the pipeline's inner client as the default.
    /// See <see cref="TypeSafeRoutingChatClient"/>.
    /// </summary>
    public static ChatClientBuilder UseTypeSafeRouter(
        this ChatClientBuilder builder,
        ITypeSafeClient typeSafeClient,
        QuestionSet questions,
        Func<TypeSafeRoutingContext, IChatClient?> select,
        Action<RoutingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(typeSafeClient);
        var options = new RoutingOptions();
        configure?.Invoke(options);
        return builder.Use(inner => new TypeSafeRoutingChatClient(typeSafeClient, questions, select, inner, options));
    }

    /// <summary>Routes each conversation to the client a selector picks, resolving <see cref="ITypeSafeClient"/> from services.</summary>
    public static ChatClientBuilder UseTypeSafeRouter(
        this ChatClientBuilder builder,
        QuestionSet questions,
        Func<TypeSafeRoutingContext, IChatClient?> select,
        Action<RoutingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new RoutingOptions();
        configure?.Invoke(options);
        return builder.Use((inner, services) => new TypeSafeRoutingChatClient(services.GetRequiredService<ITypeSafeClient>(), questions, select, inner, options));
    }
}
