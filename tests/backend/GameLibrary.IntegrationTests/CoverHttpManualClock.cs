namespace GameLibrary.IntegrationTests;

// Test-only monotonic clock with real ITimer callbacks for CTS and PeriodicTimer.
// Missed periodic ticks coalesce, as required by the cleanup host's PeriodicTimer.
public sealed class CoverHttpManualClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly HashSet<ManualTimer> _timers = [];
    private readonly DateTimeOffset _origin = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() { lock (_gate) return _origin.AddTicks(_ticks); }
    public override long GetTimestamp() { lock (_gate) return _ticks; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public bool HasTimerDueIn(TimeSpan remaining, TimeSpan period)
    {
        lock (_gate)
            return _timers.Any(timer => timer.Due == _ticks + remaining.Ticks && timer.Period == period);
    }

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by));
        ManualTimer[] callbacks;
        lock (_gate)
        {
            _ticks = checked(_ticks + by.Ticks);
            callbacks = _timers.Where(timer => timer.Due <= _ticks).ToArray();
            foreach (var timer in callbacks)
            {
                if (timer.Period > TimeSpan.Zero)
                    timer.Due = checked(timer.Due + ((_ticks - timer.Due) / timer.Period.Ticks + 1) * timer.Period.Ticks);
                else
                    timer.Due = long.MaxValue;
            }
        }
        foreach (var timer in callbacks) timer.Fire();
    }

    private sealed class ManualTimer(CoverHttpManualClock owner, TimerCallback callback, object? state) : ITimer
    {
        internal long Due = long.MaxValue;
        internal TimeSpan Period;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(period));
            lock (owner._gate)
            {
                if (_disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(owner._ticks + dueTime.Ticks);
                Period = period;
                owner._timers.Add(this);
                return true;
            }
        }

        internal void Fire()
        {
            lock (owner._gate) { if (_disposed) return; }
            callback(state);
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                _disposed = true;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
