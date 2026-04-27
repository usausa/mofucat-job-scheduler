namespace Mofucat.JobScheduler.Tests;

public sealed class JobSchedulerTest
{
    [Fact]
    public async Task StartWhenJobRunsThenUsesTimeProviderForExecutionTime()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        scheduler.AddJob("*/10 * * * * *", job, "sample");

        scheduler.Start();
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var nextRun = await job.WaitForExecutionAsync();

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task RemoveAllJobsWhenSchedulerIsRunningThenRemovesJobsAndPreventsExecution()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var firstJob = new RecordingJob();
        var secondJob = new RecordingJob();
        var firstHandle = scheduler.AddJob("*/10 * * * * *", firstJob, "first");
        var secondHandle = scheduler.AddJob("*/10 * * * * *", secondJob, "second");
        scheduler.Start();

        var removedCount = scheduler.RemoveAllJobs();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(2, removedCount);
        Assert.True(firstHandle.IsRemoved);
        Assert.True(secondHandle.IsRemoved);
        Assert.Empty(scheduler.JobNames);
        Assert.False(firstJob.HasExecuted);
        Assert.False(secondJob.HasExecuted);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task AddJobWhenSchedulerIsRunningThenJobExecutesAtNextScheduledTime()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        scheduler.Start();

        var job = new RecordingJob();
        var handle = scheduler.AddJob("*/10 * * * * *", job, "dynamic");
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var nextRun = await job.WaitForExecutionAsync();

        Assert.Equal("dynamic", handle.Name);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task RemoveJobWhenJobIsRemovedBeforeDueTimeThenJobDoesNotExecute()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var handle = scheduler.AddJob("*/10 * * * * *", job, "dynamic");
        scheduler.Start();

        var removed = handle.Remove();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.True(handle.IsRemoved);
        Assert.False(job.HasExecuted);

        await scheduler.StopAsync();
    }

    private sealed class RecordingJob : ISchedulerJob
    {
        private readonly TaskCompletionSource<DateTimeOffset> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasExecuted => completionSource.Task.IsCompleted;

        public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
        {
            completionSource.TrySetResult(time);
            return ValueTask.CompletedTask;
        }

        public Task<DateTimeOffset> WaitForExecutionAsync() => completionSource.Task;
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
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
                var due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : current + dueTime;
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
                    if (registration.IsDisposed || registration.NextTick > current)
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
                NextTick = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.GetUtcNow() + dueTime;
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
}
