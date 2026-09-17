namespace TypeSafeAI.Tests.Fakes;

/// <summary>Records the computed delays, with jitter fixed to a known value.</summary>
public sealed class RecordingRetryPolicy : RetryPolicy
{
    public List<TimeSpan> Delays { get; } = [];

    public double Random { get; set; }

    public override TimeSpan GetDelay(RetryContext context)
    {
        var delay = base.GetDelay(context);
        Delays.Add(delay);
        return delay;
    }

    protected override double NextRandom() => Random;
}
