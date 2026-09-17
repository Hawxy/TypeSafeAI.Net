namespace TypeSafeAI.Extensions.AI;

/// <summary>Ready-made guardrail policies.</summary>
public static class GuardrailPolicies
{
    /// <summary>
    /// The threshold policy from the TypeSafe guardrails cookbook. Each hazard is a noul question mapped to the action taken when
    /// its probability reaches <paramref name="actionThreshold"/>; probabilities at or above <paramref name="reviewThreshold"/>
    /// flag for review instead. When a severity score reaches <paramref name="severityBlock"/>, reviews are upgraded to blocks.
    /// The strictest triggered action wins.
    /// </summary>
    /// <param name="hazards">Noul handles and the action each triggers.</param>
    /// <param name="severity">Optional score handle rating harm potential, lowest level first.</param>
    /// <param name="actionThreshold">Probability at which a hazard triggers its action. The cookbook uses 0.70 (strict) to 0.85 (permissive).</param>
    /// <param name="reviewThreshold">Probability at which a hazard triggers a review.</param>
    /// <param name="severityBlock">Severity score at which reviews become blocks.</param>
    public static Func<GuardrailAssessment, GuardrailDecision> Thresholds(
        IReadOnlyDictionary<NoulHandle, GuardrailAction> hazards,
        ScoreHandle? severity = null,
        double actionThreshold = 0.70,
        double reviewThreshold = 0.35,
        double severityBlock = 2.0)
    {
        ArgumentNullException.ThrowIfNull(hazards);
        if (reviewThreshold > actionThreshold)
        {
            throw new ArgumentException("The review threshold must not exceed the action threshold.", nameof(reviewThreshold));
        }

        return assessment =>
        {
            var strongest = GuardrailAction.Allow;
            string? reason = null;
            var severe = severity is not null && assessment.Get(severity).Score >= severityBlock;

            foreach (var hazard in hazards)
            {
                var probability = assessment.Get(hazard.Key).Probability;
                GuardrailAction action;
                if (probability >= actionThreshold)
                {
                    action = hazard.Value;
                }
                else if (probability >= reviewThreshold)
                {
                    action = GuardrailAction.Review;
                }
                else
                {
                    continue;
                }

                if (action == GuardrailAction.Review && severe)
                {
                    action = GuardrailAction.Block;
                }

                if (action > strongest)
                {
                    strongest = action;
                    reason = $"{hazard.Key.Id} = {probability:F2}" + (severe ? " (severe)" : string.Empty);
                }
            }

            return new GuardrailDecision(strongest, reason);
        };
    }
}
