namespace Mofucat.JobScheduler;

using System.Diagnostics;
using System.Globalization;

#pragma warning disable CA1724
public sealed class JobScheduler : IAsyncDisposable
{
    public event EventHandler<JobErrorEventArgs>? JobError;

    public event EventHandler<JobCompletedEventArgs>? JobCompleted;

    private static readonly TimeSpan MaxSingleWait = TimeSpan.FromHours(1);

    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly TimeProvider timeProvider;

    private readonly Lock sync = new();

    private readonly List<ScheduledJob> jobs = [];
    private readonly Dictionary<string, ScheduledJob> jobsByName = [];
    private readonly RunningTaskCollection runningTasks = new();

    private TaskCompletionSource<bool> wakeup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? cancellationTokenSource;
    private Task? loopTask;
    private Task? stoppingLoopTask;
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

    public IReadOnlyList<IJobHandle> JobHandles
    {
        get
        {
            lock (sync)
            {
                var handles = new IJobHandle[jobs.Count];
                for (var i = 0; i < jobs.Count; i++)
                {
                    handles[i] = jobs[i].Handle;
                }

                return handles;
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
                for (var i = 0; i < jobs.Count; i++)
                {
                    names[i] = jobs[i].Name;
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
        // Dispose asynchronously after the execution loop has been stopped, without a deadline.
        _ = await StopAsync(CancellationToken.None).ConfigureAwait(false);
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

            if (stoppingLoopTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Previous loop is still stopping.");
            }

            stoppingLoopTask = null;
            isRunning = true;
            var currentCancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource = currentCancellationTokenSource;
            wakeup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var now = timeProvider.GetUtcNow();
            foreach (var job in jobs)
            {
                job.TriggerCount = 0;
                job.CatchUpCount = 0;
                job.Next = job.IsPaused ? null : job.Cron.GetNextOccurrence(now, GetTimeZone(job));
            }

            // ReSharper disable once MethodSupportsCancellation
            loopTask = Task.Run(() => RunLoopAsync(currentCancellationTokenSource.Token));
        }

        return Task.CompletedTask;
    }

    // Returns true when the scheduler stopped completely, false when waiting was abandoned because
    // cancellationToken was signaled (for example a host shutdown deadline). Even when false is
    // returned the scheduler is marked stopped and its jobs have been signaled to cancel.
    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? currentCancellationTokenSource;
        Task? currentLoopTask;

        lock (sync)
        {
            if (!isRunning)
            {
                return true;
            }

            isRunning = false;
            currentCancellationTokenSource = cancellationTokenSource;
            currentLoopTask = loopTask;
            cancellationTokenSource = null;
            loopTask = null;
            stoppingLoopTask = currentLoopTask;

            SignalWakeupUnsafe();
        }

        if (currentCancellationTokenSource is not null)
        {
            await currentCancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            if (currentLoopTask is not null)
            {
                try
                {
                    await currentLoopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!currentLoopTask.IsCompleted)
                {
                    return false;
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }
            }

            Task[] currentRunningTasks;
            lock (sync)
            {
                currentRunningTasks = runningTasks.ToArray();
            }

            if (currentRunningTasks.Length > 0)
            {
                var runningTask = Task.WhenAll(currentRunningTasks);
                try
                {
                    await runningTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!runningTask.IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            currentCancellationTokenSource?.Dispose();
        }
    }

    //--------------------------------------------------------------------------------
    // Job
    //--------------------------------------------------------------------------------

    public IJobHandle AddJob(string cronExpression, ISchedulerJob job, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0, TimeZoneInfo? timeZone = null, ConcurrencyPolicy concurrencyPolicy = ConcurrencyPolicy.Allow, TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCatchUp);
        if (timeout is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(timeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTimeout, nameof(timeout));
        }

        var expression = CronExpression.Parse(cronExpression);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var actualName = name ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            if (jobsByName.ContainsKey(actualName))
            {
                throw new ArgumentException($"A job with the same name already exists. name=[{actualName}]", nameof(name));
            }

            var scheduledJob = new ScheduledJob(this, actualName, name is not null, cronExpression, expression, job, misfirePolicy, maxCatchUp, timeZone, concurrencyPolicy, timeout);
            if (isRunning)
            {
                scheduledJob.Next = expression.GetNextOccurrence(timeProvider.GetUtcNow(), GetTimeZone(scheduledJob));
            }

            jobs.Add(scheduledJob);
            jobsByName.Add(actualName, scheduledJob);
            SignalWakeupUnsafe();

            return scheduledJob.Handle;
        }
    }

    public bool RemoveJob(string name)
    {
        lock (sync)
        {
            if (!jobsByName.TryGetValue(name, out var job))
            {
                return false;
            }

            RemoveJobUnsafe(job);
            return true;
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

    internal bool RemoveJob(ScheduledJob job)
    {
        lock (sync)
        {
            if (!jobsByName.TryGetValue(job.Name, out var current) || !ReferenceEquals(current, job))
            {
                return false;
            }

            RemoveJobUnsafe(job);
            return true;
        }
    }

    internal DateTimeOffset? GetNextExecutionTime(ScheduledJob job)
    {
        lock (sync)
        {
            return job.Handle.IsRemoved ? null : job.Next;
        }
    }

    private void RemoveJobUnsafe(ScheduledJob job)
    {
        _ = jobsByName.Remove(job.Name);
        _ = jobs.Remove(job);
        job.Handle.MarkRemoved();
        SignalWakeupUnsafe();
    }

    internal bool PauseJob(ScheduledJob job)
    {
        lock (sync)
        {
            if (job.Handle.IsRemoved || job.IsPaused)
            {
                return false;
            }

            job.IsPaused = true;
            job.Next = null;
            job.CatchUpCount = 0;
            SignalWakeupUnsafe();

            return true;
        }
    }

    internal bool ResumeJob(ScheduledJob job)
    {
        lock (sync)
        {
            if (job.Handle.IsRemoved || !job.IsPaused)
            {
                return false;
            }

            job.IsPaused = false;
            if (isRunning)
            {
                job.Next = job.Cron.GetNextOccurrence(timeProvider.GetUtcNow(), GetTimeZone(job));
                SignalWakeupUnsafe();
            }

            return true;
        }
    }

    internal bool TriggerJob(ScheduledJob job)
    {
        lock (sync)
        {
            if (!isRunning || job.Handle.IsRemoved)
            {
                return false;
            }

            job.TriggerCount++;
            SignalWakeupUnsafe();

            return true;
        }
    }

    private TimeZoneInfo GetTimeZone(ScheduledJob job) => job.TimeZone ?? timeProvider.LocalTimeZone;

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
        Activity.Current = null;
        var firingJobs = new List<(ScheduledJob Job, DateTimeOffset FireTime, bool Manual)>();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Find next execution time
            DateTimeOffset? nextTime;
            Task wakeupTask;
            lock (sync)
            {
                nextTime = null;
                foreach (var job in jobs)
                {
                    if (job.TriggerCount > 0)
                    {
                        nextTime = DateTimeOffset.MinValue;
                        break;
                    }

                    if (job.Next is null)
                    {
                        continue;
                    }

                    if ((nextTime is null) || (job.Next < nextTime))
                    {
                        nextTime = job.Next;
                    }
                }

                wakeupTask = wakeup.Task;
            }

            // Wait wakeup or next execution time
            if (nextTime is null)
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

            // Compute next execution delay
            var now = timeProvider.GetUtcNow();
            var delay = nextTime.Value - now;
            if (delay > MaxSingleWait)
            {
                delay = MaxSingleWait;
            }

            // Wait until the scheduled time
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

                // Recompute now and check if the next execution time has arrived
                now = timeProvider.GetUtcNow();
                if (now < nextTime.Value)
                {
                    continue;
                }
            }

            // Gather jobs to fire and update their next execution time
            firingJobs.Clear();
            lock (sync)
            {
                foreach (var job in jobs)
                {
                    while (job.TriggerCount > 0)
                    {
                        job.TriggerCount--;
                        firingJobs.Add((job, TimeZoneInfo.ConvertTime(now, GetTimeZone(job)), true));
                    }

                    // Skip future jobs
                    if ((job.Next is null) || (job.Next > now))
                    {
                        continue;
                    }

                    // Compute the next execution time for the job
                    var timeZone = GetTimeZone(job);
                    var scheduledTime = job.Next.Value;
                    var following = job.Cron.GetNextOccurrence(scheduledTime, timeZone);
                    var late = following <= now;
                    var fireTime = TimeZoneInfo.ConvertTime(late ? now : scheduledTime, timeZone);

                    firingJobs.Add((job, fireTime, false));

                    // Update the next execution time
                    if (!late)
                    {
                        // Normal firing
                        job.CatchUpCount = 0;
                        job.Next = following;
                    }
                    else if (job.MisfirePolicy == MisfirePolicy.Skip)
                    {
                        // Backlog is skipped
                        job.CatchUpCount = 0;
                        job.Next = job.Cron.GetNextOccurrence(now, timeZone);
                    }
                    else
                    {
                        // CatchUp firing
                        job.CatchUpCount++;
                        if ((job.MaxCatchUp > 0) && (job.CatchUpCount >= job.MaxCatchUp))
                        {
                            // Limit reached: drop the remaining backlog
                            job.CatchUpCount = 0;
                            job.Next = job.Cron.GetNextOccurrence(now, timeZone);
                        }
                        else
                        {
                            // Below the limit: advance from the scheduled time
                            job.Next = following;
                        }
                    }
                }
            }

            // Fire jobs
            foreach (var (job, fireTime, manual) in firingJobs)
            {
                if (job.Handle.IsRemoved || (!manual && job.IsPaused))
                {
                    continue;
                }

                if (!job.TryBeginExecution())
                {
                    SchedulerDiagnostics.Skip(job.DiagnosticName);
                    continue;
                }

                TrackTask(FireJobAsync(job, fireTime, cancellationToken));
            }
        }
    }

    private void TrackTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (sync)
        {
            runningTasks.Add(task);
        }

        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                var scheduler = (JobScheduler)state!;
                lock (scheduler.sync)
                {
                    scheduler.runningTasks.Remove(completedTask);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, timeProvider, delayCancellation.Token);
        var completedTask = await Task.WhenAny(wakeupTask, delayTask).ConfigureAwait(false);
        if (ReferenceEquals(completedTask, delayTask))
        {
            if (delayTask.IsCanceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        else
        {
            await delayCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task FireJobAsync(ScheduledJob job, DateTimeOffset fireTime, CancellationToken cancellationToken)
    {
        var activity = SchedulerDiagnostics.StartExecution(job.DiagnosticName);
        var timestamp = timeProvider.GetTimestamp();
        Exception? exception = null;
#pragma warning disable CA1031
        try
        {
            if (job.Timeout is { } timeout)
            {
                await ExecuteWithTimeoutAsync(job, fireTime, timeout, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await job.Job.ExecuteAsync(fireTime, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            exception = ex;
        }
#pragma warning restore CA1031

        var duration = timeProvider.GetElapsedTime(timestamp);
        job.CompleteExecution(fireTime, duration, exception);
        SchedulerDiagnostics.RecordExecution(activity, job.DiagnosticName, duration, exception);

        if ((exception is not null) && (JobError is { } errorHandler))
        {
            InvokeHandler(errorHandler, new JobErrorEventArgs(job.Name, exception));
        }

        if (JobCompleted is { } completedHandler)
        {
            InvokeHandler(completedHandler, new JobCompletedEventArgs(job.Name, fireTime, duration, exception));
        }

        SchedulerDiagnostics.StopExecution(activity);
        job.EndExecution();
    }

    private void InvokeHandler<TEventArgs>(EventHandler<TEventArgs> handler, TEventArgs args)
    {
#pragma warning disable CA1031
        try
        {
            handler(this, args);
        }
        catch
        {
            // Event handlers must not fault the job task and StopAsync.
        }
#pragma warning restore CA1031
    }

    private async ValueTask ExecuteWithTimeoutAsync(ScheduledJob job, DateTimeOffset fireTime, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await job.Job.ExecuteAsync(fireTime, linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Job execution timed out. name=[{job.Name}], timeout=[{timeout}]", ex);
        }
    }

    private sealed class RunningTaskCollection
    {
        private const int InitialCapacity = 4;

        private Task?[] tasks = new Task?[InitialCapacity];
        private int count;

        public void Add(Task task)
        {
            for (var i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] is null)
                {
                    tasks[i] = task;
                    count++;
                    return;
                }
            }

            var newTasks = new Task?[tasks.Length * 2];
            Array.Copy(tasks, newTasks, tasks.Length);
            newTasks[tasks.Length] = task;
            tasks = newTasks;
            count++;
        }

        public void Remove(Task task)
        {
            for (var i = 0; i < tasks.Length; i++)
            {
                if (!ReferenceEquals(tasks[i], task))
                {
                    continue;
                }

                tasks[i] = null;
                count--;
                return;
            }
        }

        public Task[] ToArray()
        {
            if (count == 0)
            {
                return [];
            }

            var result = new Task[count];
            var writeIndex = 0;
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] is { } task)
                {
                    result[writeIndex++] = task;
                }
            }

            return result;
        }
    }
}
#pragma warning restore CA1724
