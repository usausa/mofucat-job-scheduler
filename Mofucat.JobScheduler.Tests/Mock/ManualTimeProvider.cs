namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock sync = new();

    private readonly ConcurrentDictionary<long, TimerRegistration> timers = [];

    private long nextId;

    private DateTimeOffset current = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (sync)
        {
            return current;
        }
    }

    public override long GetTimestamp() => GetUtcNow().Ticks;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        TimerRegistration registration;
        lock (sync)
        {
            DateTimeOffset due;
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                due = DateTimeOffset.MaxValue;
            }
            else
            {
                due = current + dueTime;
            }

            registration = new TimerRegistration(Interlocked.Increment(ref nextId), this, callback, state, due, period);
            timers[registration.Id] = registration;
        }

        return registration;
    }

    public void Advance(TimeSpan amount)
    {
        List<(TimerCallback Callback, object? State)> dueCallbacks = [];
        lock (sync)
        {
            current += amount;
            foreach (var registration in timers.Values)
            {
                if (registration.IsDisposed || (registration.NextTick > current))
                {
                    continue;
                }

                dueCallbacks.Add((registration.Callback, registration.State));
                if (registration.Period == Timeout.InfiniteTimeSpan)
                {
                    registration.Dispose();
                }
                else
                {
                    registration.NextTick = current + registration.Period;
                }
            }
        }

        foreach (var (callback, state) in dueCallbacks)
        {
            callback(state);
        }
    }

    private void Remove(long id) => timers.TryRemove(id, out _);

    private sealed class TimerRegistration(long id, ManualTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset nextTick, TimeSpan period) : ITimer
    {
        private int disposed;

        public long Id { get; } = id;

        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public TimeSpan Period { get; private set; } = period;

        public DateTimeOffset NextTick { get; set; } = nextTick;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed)
            {
                return false;
            }

            Period = period;
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                NextTick = DateTimeOffset.MaxValue;
            }
            else
            {
                NextTick = owner.GetUtcNow() + dueTime;
            }

            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Remove(Id);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
