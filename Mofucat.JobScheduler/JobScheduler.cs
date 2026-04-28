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
    private readonly RunningTaskCollection runningTasks = new();

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

    public async Task StopAsync()
    {
        CancellationTokenSource? currentCancellationTokenSource;
        Task? currentLoopTask;
        Task[] currentRunningTasks;

        lock (sync)
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            currentCancellationTokenSource = cancellationTokenSource;
            currentLoopTask = loopTask;
            currentRunningTasks = runningTasks.ToArray();
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

        if (currentRunningTasks.Length > 0)
        {
            await Task.WhenAll(currentRunningTasks).ConfigureAwait(false);
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

            if (nextTime is null)
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
            var delay = nextTime.Value - now;
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
                if (now < nextTime.Value)
                {
                    continue;
                }
            }

            firingJobs.Clear();
            lock (sync)
            {
                foreach (var job in jobs)
                {
                    if ((job.Next is null) || (job.Next > now))
                    {
                        continue;
                    }

                    var scheduledTime = job.Next.Value;
                    var fireTime = scheduledTime;
                    if (scheduledTime < now)
                    {
                        fireTime = now;
                    }

                    firingJobs.Add((job, fireTime));
                    job.Next = job.Cron.GetNextOccurrence(scheduledTime);
                }
            }

            foreach (var (job, fireTime) in firingJobs)
            {
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

        var delayTask = Task.Delay(delay, timeProvider, cancellationToken);
        var completedTask = await Task.WhenAny(wakeupTask, delayTask).ConfigureAwait(false);
        if (ReferenceEquals(completedTask, delayTask) && delayTask.IsCanceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
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
