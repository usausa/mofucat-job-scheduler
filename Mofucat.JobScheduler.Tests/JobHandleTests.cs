namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class JobHandleTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PauseWhenSchedulerIsRunningThenSkipsOccurrencesUntilResumed()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var synchronizationJob = new CountingJob();
        var handle = scheduler.AddJob("*/10 * * * * *", job, "sample");
        scheduler.AddJob("*/10 * * * * *", synchronizationJob, "synchronization");
        await scheduler.StartAsync();

        Assert.True(handle.Pause());
        Assert.False(handle.Pause());
        Assert.True(handle.IsPaused);
        Assert.Null(handle.NextExecutionTime);

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        await synchronizationJob.WaitForExecutionsAsync(1, cancellationTokenSource.Token);

        Assert.False(job.HasExecuted);

        Assert.True(handle.Resume());
        Assert.False(handle.Resume());
        Assert.False(handle.IsPaused);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 20, TimeSpan.Zero), handle.NextExecutionTime);

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        var executionTime = await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 20, TimeSpan.Zero), executionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenJobIsPausedThenDoesNotScheduleJob()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("*/10 * * * * *", new NopJob(), "sample");
        handle.Pause();

        await scheduler.StartAsync();

        Assert.Null(handle.NextExecutionTime);

        handle.Resume();

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), handle.NextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PauseWhenJobIsPausedAfterDueTimeButBeforeFiringThenPreventsExecution()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var target = new RecordingJob();
        var targetHolder = new StrongBox<IJobHandle?>();
        var trigger = new CallbackJob(() => targetHolder.Value?.Pause());
        scheduler.AddJob("*/10 * * * * *", trigger, "trigger");
        var targetHandle = scheduler.AddJob("*/10 * * * * *", target, "target");
        targetHolder.Value = targetHandle;
        await scheduler.StartAsync();

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        await trigger.WaitForExecutionAsync(cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        Assert.True(targetHandle.IsPaused);
        Assert.False(target.HasExecuted);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TriggerWhenSchedulerIsRunningThenExecutesJobAtCurrentTime()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero), TestTimeZones.Tokyo);
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily");
        await scheduler.StartAsync();

        var triggered = handle.Trigger();
        var executionTime = await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        Assert.True(triggered);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero), executionTime);
        Assert.Equal(TimeSpan.FromHours(9), executionTime.Offset);
        Assert.Equal(new DateTimeOffset(2026, 4, 27, 3, 0, 0, TimeSpan.FromHours(9)), handle.NextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TriggerWhenJobIsPausedThenExecutesJobWithoutResuming()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var handle = scheduler.AddJob("*/10 * * * * *", job, "sample");
        await scheduler.StartAsync();
        handle.Pause();

        var triggered = handle.Trigger();
        await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        Assert.True(triggered);
        Assert.True(handle.IsPaused);
        Assert.Null(handle.NextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TriggerWhenSchedulerIsNotRunningOrJobIsRemovedThenReturnsFalse()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("*/10 * * * * *", new NopJob(), "sample");

        Assert.False(handle.Trigger());

        await scheduler.StartAsync();
        handle.Remove();

        Assert.False(handle.Trigger());
        Assert.False(handle.Pause());
        Assert.False(handle.Resume());

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IsRunningWhenJobIsExecutingThenRecordsLastExecutionAfterCompletion()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily");
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);

        Assert.True(handle.IsRunning);
        Assert.Null(handle.LastExecution);

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(3), cancellationTokenSource.Token);
        job.Release();
        await WaitUntilAsync(() => !handle.IsRunning, cancellationTokenSource.Token);

        var last = Assert.NotNull(handle.LastExecution);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero), last.Time);
        Assert.Equal(TimeSpan.FromSeconds(3), last.Duration);
        Assert.Null(last.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LastExecutionWhenJobThrowsThenHoldsReportedException()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), "daily");
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        var error = await errorSource.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Same(error.Exception, handle.LastExecution?.Exception);

        await WaitUntilAsync(() => !handle.IsRunning, cancellationTokenSource.Token);
        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveWhenJobWithSameNameIsAddedAgainThenStaleHandleDoesNotAffectNewJob()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var oldHandle = scheduler.AddJob("0 3 * * *", new NopJob(), "sample");
        await scheduler.StartAsync();
        oldHandle.Remove();
        var newHandle = scheduler.AddJob("0 4 * * *", new NopJob(), "sample");

        Assert.Null(oldHandle.NextExecutionTime);
        Assert.False(oldHandle.Remove());
        Assert.False(newHandle.IsRemoved);
        Assert.Same(newHandle, scheduler.FindJob("sample"));
        Assert.Equal(new DateTimeOffset(2026, 4, 27, 4, 0, 0, TimeSpan.Zero), newHandle.NextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    private static CancellationTokenSource CreateCancellationTokenSource()
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationTokenSource.CancelAfter(WaitTimeout);
        return cancellationTokenSource;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }
}
