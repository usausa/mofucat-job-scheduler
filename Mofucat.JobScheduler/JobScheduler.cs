namespace Mofucat.JobScheduler;

using System.Globalization;

public sealed class JobScheduler : IAsyncDisposable
{
    public event EventHandler<JobErrorEventArgs>? JobError;

    private static readonly TimeSpan MaxSingleWait = TimeSpan.FromHours(1);

    private readonly TimeProvider timeProvider;

    private readonly Lock sync = new();

    private readonly List<ScheduledJob> jobs = [];
    private readonly Dictionary<string, ScheduledJob> jobsByName = [];

    private TaskCompletionSource<bool> wakeup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? cancellationTokenSource;
    private Task? loopTask;
    private bool isRunning;

    private bool disposed;

    //--------------------------------------------------------------------------------
    // Property
    //--------------------------------------------------------------------------------

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return isRunning;
            }
        }
    }

    public IReadOnlyList<string> JobNames
    {
        get
        {
            lock (sync)
            {
                var names = new string[jobs.Count];
                for (var index = 0; index < jobs.Count; index++)
                {
                    names[index] = jobs[index].Name;
                }

                return names;
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

    public JobScheduler(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    //--------------------------------------------------------------------------------
    // Dispose
    //--------------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        // Dispose asynchronously after the execution loop has been stopped.
        await StopAsync().ConfigureAwait(false);
        lock (sync)
        {
            disposed = true;
        }
    }

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    public Task StartAsync()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (isRunning)
            {
                return Task.CompletedTask;
            }

            isRunning = true;
            cancellationTokenSource = new CancellationTokenSource();
            wakeup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var now = timeProvider.GetUtcNow();
            foreach (var job in jobs)
            {
                job.Next = job.Cron.GetNextOccurrence(now);
            }

            loopTask = Task.Run(() => RunLoopAsync(cancellationTokenSource.Token));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? currentCancellationTokenSource;
        Task? currentLoopTask;

        lock (sync)
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            currentCancellationTokenSource = cancellationTokenSource;
            currentLoopTask = loopTask;
            cancellationTokenSource = null;
            loopTask = null;

            SignalWakeupUnsafe();
        }

        if (currentCancellationTokenSource is not null)
        {
            await currentCancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }

        if (currentLoopTask is not null)
        {
            try
            {
                await currentLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
        }

        currentCancellationTokenSource?.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Job
    //--------------------------------------------------------------------------------

    public IJobHandle AddJob(string cronExpression, ISchedulerJob job, string? name = null)
    {
        var expression = CronExpression.Parse(cronExpression);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var actualName = name ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            if (jobsByName.ContainsKey(actualName))
            {
                throw new ArgumentException($"A job with the same name already exists. name=[{actualName}]", nameof(name));
            }

            var handle = new JobHandle(this, actualName, cronExpression);
            var scheduledJob = new ScheduledJob(actualName, expression, job, handle);
            if (isRunning)
            {
                scheduledJob.Next = expression.GetNextOccurrence(timeProvider.GetUtcNow());
            }

            jobs.Add(scheduledJob);
            jobsByName.Add(actualName, scheduledJob);
            SignalWakeupUnsafe();

            return handle;
        }
    }

    public bool RemoveJob(string name)
    {
        lock (sync)
        {
            if (jobsByName.Remove(name, out var job))
            {
                _ = jobs.Remove(job);
                job.Handle.MarkRemoved();
                SignalWakeupUnsafe();

                return true;
            }

            return false;
        }
    }

    public int RemoveAllJobs()
    {
        lock (sync)
        {
            var removedCount = jobs.Count;
            if (removedCount == 0)
            {
                return 0;
            }

            foreach (var job in jobs)
            {
                job.Handle.MarkRemoved();
            }

            jobs.Clear();
            jobsByName.Clear();
            SignalWakeupUnsafe();

            return removedCount;
        }
    }

    public IJobHandle? FindJob(string name)
    {
        lock (sync)
        {
            return jobsByName.GetValueOrDefault(name)?.Handle;
        }
    }

    //--------------------------------------------------------------------------------
    // Execution loop
    //--------------------------------------------------------------------------------

    private void SignalWakeupUnsafe()
    {
        var currentWakeup = wakeup;
        wakeup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentWakeup.TrySetResult(true);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ScheduledJob? nextJob;
            DateTimeOffset? nextTime;
            Task wakeupTask;
            lock (sync)
            {
                nextJob = null;
                nextTime = null;
                foreach (var job in jobs)
                {
                    if (job.Next is null)
                    {
                        continue;
                    }

                    if ((nextTime is null) || (job.Next < nextTime))
                    {
                        nextTime = job.Next;
                        nextJob = job;
                    }
                }

                wakeupTask = wakeup.Task;
            }

            if (nextJob is null)
            {
                // Wait
                try
                {
                    await WaitAsync(wakeupTask, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var now = timeProvider.GetUtcNow();
            var delay = nextTime!.Value - now;
            if (delay > MaxSingleWait)
            {
                delay = MaxSingleWait;
            }

            // Wait
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await WaitAsync(wakeupTask, delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                now = timeProvider.GetUtcNow();
            }

            if (now < nextTime.Value)
            {
                continue;
            }

            ScheduledJob? firingJob = null;
            var fireTime = nextTime.Value;
            lock (sync)
            {
                if (jobsByName.TryGetValue(nextJob.Name, out var current) && ReferenceEquals(current, nextJob) && (current.Next == nextTime))
                {
                    firingJob = current;
                    current.Next = current.Cron.GetNextOccurrence(timeProvider.GetUtcNow());
                }
            }

            if (firingJob is not null)
            {
                // Run async
                _ = FireJobAsync(firingJob, fireTime, cancellationToken);
            }
        }
    }

    private async Task WaitAsync(Task wakeupTask, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay == Timeout.InfiniteTimeSpan)
        {
            // Wait wakeup or Cancel
            var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationTaskSource))
            {
                await Task.WhenAny(wakeupTask, cancellationTaskSource.Task).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        // Wait timer
        var delayTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA2007
        await using var timer = timeProvider.CreateTimer(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            delayTaskSource,
            delay,
            Timeout.InfiniteTimeSpan);
#pragma warning restore CA2007
#pragma warning disable CA2007
        await using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            delayTaskSource);
#pragma warning restore CA2007

        await Task.WhenAny(wakeupTask, delayTaskSource.Task).ConfigureAwait(false);

        try
        {
            await delayTaskSource.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task FireJobAsync(ScheduledJob job, DateTimeOffset fireTime, CancellationToken cancellationToken)
    {
#pragma warning disable CA1031
        try
        {
            await job.Job.ExecuteAsync(fireTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            JobError?.Invoke(this, new JobErrorEventArgs(job.Name, ex));
        }
#pragma warning restore CA1031
    }
}
