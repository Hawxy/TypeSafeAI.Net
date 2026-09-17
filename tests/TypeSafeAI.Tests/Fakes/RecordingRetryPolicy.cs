namespace TypeSafeAI.Tests.Fakes;

/// <summary>Records computed delays instead of sleeping, with jitter fixed to a known value.</summary>
public sealed class RecordingRetryPolicy : RetryPolicy
{
    public List<TimeSpan> Delays { get; } = [];

    public double Random { get; set; }

    protected internal override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delays.Add(delay);
        return Task.CompletedTask;
    }

    protected override double NextRandom() => Random;
}
