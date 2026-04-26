namespace Mofucat.JobScheduler;

using System.Globalization;

/// <summary>
/// Schedules and executes registered jobs.
/// </summary>
public sealed class JobScheduler : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan MaxSingleWait = TimeSpan.FromHours(1);

    private readonly Lock sync = new();
    private readonly Dictionary<string, ScheduledJob> jobs = [];
    private TaskCompletionSource<bool> wakeup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? cancellationTokenSource;
    private Task? loopTask;
    private bool isRunning;
    private bool disposed;

    /// <summary>
    /// Occurs when a job execution fails.
    /// </summary>
    public event EventHandler<JobErrorEventArgs>? JobError;

    /// <summary>
    /// Gets a value indicating whether the scheduler is running.
    /// </summary>
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

    /// <summary>
    /// Gets the registered job names.
    /// </summary>
    public IReadOnlyList<string> JobNames
    {
        get
        {
            lock (sync)
            {
                var names = new string[jobs.Count];
                jobs.Keys.CopyTo(names, 0);
                return names;
            }
        }
    }

    /// <summary>
    /// Starts the scheduler.
    /// </summary>
    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (isRunning)
            {
                return;
            }

            isRunning = true;
            cancellationTokenSource = new CancellationTokenSource();
            wakeup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var now = DateTimeOffset.Now;
            foreach (var job in jobs.Values)
            {
                job.Next = job.Cron.GetNextOccurrence(now);
            }

            loopTask = Task.Run(() => RunLoopAsync(cancellationTokenSource.Token));
        }
    }

    /// <summary>
    /// Stops the scheduler.
    /// </summary>
    /// <returns>A task that completes when the scheduler stops.</returns>
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

        currentCancellationTokenSource?.Cancel();
        if (currentLoopTask is not null)
        {
            try
            {
                await currentLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        currentCancellationTokenSource?.Dispose();
    }

    /// <summary>
    /// Stops the scheduler synchronously.
    /// </summary>
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Adds a job to the scheduler.
    /// </summary>
    /// <param name="cronExpression">The cron expression.</param>
    /// <param name="job">The job instance.</param>
    /// <param name="name">The optional job name.</param>
    /// <returns>A handle for the registered job.</returns>
    public IJobHandle AddJob(string cronExpression, ISchedulerJob job, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentNullException.ThrowIfNull(job);

        var expression = CronExpression.Parse(cronExpression);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var actualName = name ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            if (jobs.ContainsKey(actualName))
            {
                throw new ArgumentException($"A job named '{actualName}' already exists.", nameof(name));
            }

            var handle = new JobHandle(this, actualName, cronExpression);
            var scheduledJob = new ScheduledJob(actualName, expression, job, handle);
            if (isRunning)
            {
                scheduledJob.Next = expression.GetNextOccurrence(DateTimeOffset.Now);
            }

            jobs[actualName] = scheduledJob;
            SignalWakeupUnsafe();
            return handle;
        }
    }

    /// <summary>
    /// Removes a registered job.
    /// </summary>
    /// <param name="name">The job name.</param>
    /// <returns><see langword="true" /> when the job was removed.</returns>
    public bool RemoveJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (sync)
        {
            if (jobs.TryGetValue(name, out var job))
            {
                jobs.Remove(name);
                job.Handle.MarkRemoved();
                SignalWakeupUnsafe();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Determines whether the specified job is registered.
    /// </summary>
    /// <param name="name">The job name.</param>
    /// <returns><see langword="true" /> when the job exists.</returns>
    public bool ContainsJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (sync)
        {
            return jobs.ContainsKey(name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        lock (sync)
        {
            disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (sync)
        {
            disposed = true;
        }
    }

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
                foreach (var job in jobs.Values)
                {
                    if (job.Next is null)
                    {
                        continue;
                    }

                    if (nextTime is null || job.Next < nextTime)
                    {
                        nextTime = job.Next;
                        nextJob = job;
                    }
                }

                wakeupTask = wakeup.Task;
            }

            if (nextJob is null)
            {
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

            var now = DateTimeOffset.Now;
            var delay = nextTime!.Value - now;
            if (delay > MaxSingleWait)
            {
                delay = MaxSingleWait;
            }

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

                now = DateTimeOffset.Now;
            }

            if (now < nextTime.Value)
            {
                continue;
            }

            ScheduledJob? firingJob = null;
            var fireTime = nextTime.Value;
            lock (sync)
            {
                if (jobs.TryGetValue(nextJob.Name, out var current)
                    && ReferenceEquals(current, nextJob)
                    && current.Next == nextTime)
                {
                    firingJob = current;
                    current.Next = current.Cron.GetNextOccurrence(DateTimeOffset.Now);
                }
            }

            if (firingJob is not null)
            {
                _ = FireJobAsync(firingJob, fireTime, cancellationToken);
            }
        }
    }

    private static async Task WaitAsync(Task wakeupTask, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay == Timeout.InfiniteTimeSpan)
        {
            var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationTaskSource))
            {
                await Task.WhenAny(wakeupTask, cancellationTaskSource.Task).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, linkedCancellationTokenSource.Token);
        await Task.WhenAny(wakeupTask, delayTask).ConfigureAwait(false);
        await linkedCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await delayTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task FireJobAsync(ScheduledJob job, DateTimeOffset fireTime, CancellationToken cancellationToken)
    {
        try
        {
            await job.Job.ExecuteAsync(fireTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            JobError?.Invoke(this, new JobErrorEventArgs(job.Name, exception));
        }
    }
}
