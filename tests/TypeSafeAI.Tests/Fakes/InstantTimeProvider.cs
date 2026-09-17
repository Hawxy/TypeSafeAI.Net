namespace TypeSafeAI.Tests.Fakes;

/// <summary>A clock whose timers fire as soon as they are armed, so retry delays and timeouts never wait.</summary>
public sealed class InstantTimeProvider : TimeProvider
{
    public static InstantTimeProvider Instance { get; } = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new InstantTimer(callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class InstantTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }

            return true;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;
    }
}
