namespace Mofucat.JobScheduler;

using System.Globalization;

#pragma warning disable CA1724
public sealed class JobScheduler : IAsyncDisposable
{
    public event EventHandler<JobErrorEventArgs>? JobError;

    private static readonly TimeSpan MaxSingleWait = TimeSpan.FromHours(1);

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
                job.Next = job.Cron.GetNextOccurrence(now);
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

    public IJobHandle AddJob(string cronExpression, ISchedulerJob job, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCatchUp);

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
            var scheduledJob = new ScheduledJob(actualName, expression, job, handle, misfirePolicy, maxCatchUp);
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

    internal DateTimeOffset? GetNextExecutionTime(string name)
    {
        lock (sync)
        {
            if (jobsByName.TryGetValue(name, out var job))
            {
                return job.Next;
            }

            return null;
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
        var firingJobs = new List<(ScheduledJob Job, DateTimeOffset FireTime)>();

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
                    // Skip future jobs
                    if ((job.Next is null) || (job.Next > now))
                    {
                        continue;
                    }

                    // Compute the next execution time for the job
                    var scheduledTime = job.Next.Value;
                    var late = scheduledTime < now;
                    var fireTime = late ? now : scheduledTime;

                    firingJobs.Add((job, fireTime));

                    // Update the next execution time
                    if (job.MisfirePolicy == MisfirePolicy.Skip)
                    {
                        // Backlog is skipped
                        job.CatchUpCount = 0;
                        job.Next = job.Cron.GetNextOccurrence(now);
                    }
                    else if (!late)
                    {
                        // Normal firing
                        job.CatchUpCount = 0;
                        job.Next = job.Cron.GetNextOccurrence(scheduledTime);
                    }
                    else
                    {
                        // CatchUp firing
                        job.CatchUpCount++;
                        if ((job.MaxCatchUp > 0) && (job.CatchUpCount >= job.MaxCatchUp))
                        {
                            // Limit reached: drop the remaining backlog
                            job.CatchUpCount = 0;
                            job.Next = job.Cron.GetNextOccurrence(now);
                        }
                        else
                        {
                            // Below the limit: advance from the scheduled time
                            job.Next = job.Cron.GetNextOccurrence(scheduledTime);
                        }
                    }
                }
            }

            // Fire jobs
            foreach (var (job, fireTime) in firingJobs)
            {
                if (job.Handle.IsRemoved)
                {
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
#pragma warning disable CA1031
        try
        {
            await job.Job.ExecuteAsync(fireTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            try
            {
                JobError?.Invoke(this, new JobErrorEventArgs(job.Name, ex));
            }
            catch
            {
                // JobError handler must not fault the job task and StopAsync.
            }
        }
#pragma warning restore CA1031
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
