using Microsoft.Extensions.AI.Evaluation;

namespace TypeSafeAI.Extensions.AI.Evaluation;

/// <summary>Ready-made interpretation rules for <see cref="TypeSafeEvaluatorOptions.Interpret"/>.</summary>
public static class TypeSafeInterpretations
{
    /// <summary>Combines rules keyed by question id; metrics without a rule get no interpretation.</summary>
    public static Func<TypeSafeMetricContext, EvaluationMetricInterpretation?> ByQuestion(
        IReadOnlyDictionary<string, Func<TypeSafeMetricContext, EvaluationMetricInterpretation?>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return context => rules.TryGetValue(context.QuestionId, out var rule) ? rule(context) : null;
    }

    /// <summary>Passes when a score answer reaches the level index; rates by how far along the scale it sits.</summary>
    public static Func<TypeSafeMetricContext, EvaluationMetricInterpretation?> ScoreAtLeast(int level) => context =>
    {
        if (context.Answer is not ScoreAnswer score)
        {
            return null;
        }

        var levels = Math.Max(1, context.Question is ScoreQuestion q ? q.LevelCount - 1 : score.Legend.Count - 1);
        var fraction = Math.Clamp(score.Score / levels, 0, 1);
        var failed = score.Score < level;
        return new EvaluationMetricInterpretation(Rate(fraction), failed, failed ? $"Score {score.Score:F2} is below level {level}." : null);
    };

    /// <summary>Passes when the probability of yes is at most the threshold, for questions where yes means a problem.</summary>
    public static Func<TypeSafeMetricContext, EvaluationMetricInterpretation?> NoulAtMost(double threshold) => context =>
    {
        if (context.Answer is not NoulAnswer noul)
        {
            return null;
        }

        var failed = noul.Probability > threshold;
        return new EvaluationMetricInterpretation(Rate(1 - noul.Probability), failed, failed ? $"Probability {noul.Probability:F2} exceeds {threshold:F2}." : null);
    };

    /// <summary>Passes when the probability of yes is at least the threshold, for questions where yes means success.</summary>
    public static Func<TypeSafeMetricContext, EvaluationMetricInterpretation?> NoulAtLeast(double threshold) => context =>
    {
        if (context.Answer is not NoulAnswer noul)
        {
            return null;
        }

        var failed = noul.Probability < threshold;
        return new EvaluationMetricInterpretation(Rate(noul.Probability), failed, failed ? $"Probability {noul.Probability:F2} is below {threshold:F2}." : null);
    };

    /// <summary>Passes when the chosen label is one of the given labels.</summary>
    public static Func<TypeSafeMetricContext, EvaluationMetricInterpretation?> ChoiceIn(params string[] passingLabels)
    {
        ArgumentNullException.ThrowIfNull(passingLabels);
        var passing = new HashSet<string>(passingLabels, StringComparer.Ordinal);
        return context =>
        {
            if (context.Answer is not ChoiceAnswer choice)
            {
                return null;
            }

            var failed = !passing.Contains(choice.Choice);
            return new EvaluationMetricInterpretation(
                failed ? EvaluationRating.Unacceptable : Rate(choice.Confidence),
                failed,
                failed ? $"Choice '{choice.Choice}' is not one of {string.Join(", ", passing)}." : null);
        };
    }

    // Maps a 0..1 goodness fraction onto the rating scale.
    private static EvaluationRating Rate(double fraction) => fraction switch
    {
        >= 0.9 => EvaluationRating.Exceptional,
        >= 0.7 => EvaluationRating.Good,
        >= 0.5 => EvaluationRating.Average,
        >= 0.3 => EvaluationRating.Poor,
        _ => EvaluationRating.Unacceptable,
    };
}
