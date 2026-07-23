namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class JobSchedulerTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartWhenJobRunsThenUsesTimeProviderForExecutionTime()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        scheduler.AddJob("*/10 * * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);

        var nextRun = await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenUsingSecondScheduleAtExactSecondThenFirstExecutionOccursAtNextSecond()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 0, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        scheduler.AddJob("*/1 * * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(1), cancellationTokenSource.Token);

        var nextRun = await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 1, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenUsingSecondScheduleAtFractionalSecondThenFirstExecutionOccursAfterStartSecond()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 0, 1, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        scheduler.AddJob("*/1 * * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromMilliseconds(998), cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromMilliseconds(1), cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromMilliseconds(1), cancellationTokenSource.Token);
        var nextRun = await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        // Assert
        Assert.True(nextRun > new DateTimeOffset(2026, 4, 26, 10, 7, 0, 1, TimeSpan.Zero));
        Assert.Equal(1, nextRun.Second);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveAllJobsWhenSchedulerIsRunningThenRemovesJobsAndPreventsExecution()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var firstJob = new RecordingJob();
        var secondJob = new RecordingJob();
        var firstHandle = scheduler.AddJob("*/10 * * * * *", firstJob, "first");
        var secondHandle = scheduler.AddJob("*/10 * * * * *", secondJob, "second");
        await scheduler.StartAsync();

        // Act
        var removedCount = scheduler.RemoveAllJobs();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.Equal(2, removedCount);
        Assert.True(firstHandle.IsRemoved);
        Assert.True(secondHandle.IsRemoved);
        Assert.Empty(scheduler.JobNames);
        Assert.False(firstJob.HasExecuted);
        Assert.False(secondJob.HasExecuted);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddJobWhenSchedulerIsRunningThenJobExecutesAtNextScheduledTime()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        await scheduler.StartAsync();

        var job = new RecordingJob();

        // Act
        var handle = scheduler.AddJob("*/10 * * * * *", job, "dynamic");

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);

        var nextRun = await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        // Assert
        Assert.Equal("dynamic", handle.Name);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemoveJobWhenJobIsRemovedBeforeDueTimeThenReturnsRemovedHandleAndPreventsExecution()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var handle = scheduler.AddJob("*/10 * * * * *", job, "dynamic");
        await scheduler.StartAsync();

        // Act
        var removed = handle.Remove();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.True(removed);
        Assert.True(handle.IsRemoved);
        Assert.False(job.HasExecuted);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FindJobWhenJobExistsThenReturnsRegisteredHandle()
    {
        // Arrange
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler();
#pragma warning restore CA2007
        var registeredHandle = scheduler.AddJob("*/10 * * * * *", new RecordingJob(), "sample");

        // Act
        var handle = scheduler.FindJob("sample");

        // Assert
        Assert.Same(registeredHandle, handle);
    }

    [Fact]
    public async Task FindJobWhenJobDoesNotExistThenReturnsNull()
    {
        // Arrange
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler();
#pragma warning restore CA2007

        // Act
        var handle = scheduler.FindJob("missing");

        // Assert
        Assert.Null(handle);
    }

    [Fact]
    public async Task JobHandlesWhenJobsExistThenReturnsAllRegisteredHandles()
    {
        // Arrange
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler();
#pragma warning restore CA2007
        var firstHandle = scheduler.AddJob("*/10 * * * * *", new RecordingJob(), "first");
        var secondHandle = scheduler.AddJob("*/15 * * * * *", new RecordingJob(), "second");

        // Act
        var handles = scheduler.JobHandles;

        // Assert
        Assert.Equal(2, handles.Count);
        Assert.Contains(handles, static handle => handle.Name == "first");
        Assert.Contains(handles, static handle => handle.Name == "second");
        Assert.Contains(firstHandle, handles);
        Assert.Contains(secondHandle, handles);
    }

    [Fact]
    public async Task NextExecutionTimeWhenSchedulerIsRunningThenReturnsScheduledTimeFromHandle()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("*/10 * * * * *", new RecordingJob(), "sample");
        await scheduler.StartAsync();

        // Act
        var nextExecutionTime = handle.NextExecutionTime;

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddJobWhenUsingMinuteScheduleThenJobDoesNotRepeatWithoutTimeAdvancing()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        scheduler.AddJob("*/1 * * * *", job, "sample");
        await scheduler.StartAsync();

        // Act
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(30), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.Equal(1, job.ExecutionCount);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenUsingMinuteScheduleMidMinuteThenFirstExecutionWaitsUntilNextMinute()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        scheduler.AddJob("*/1 * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(29), cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.False(job.HasExecuted);

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(1), cancellationTokenSource.Token);
        var nextRun = await job.WaitForExecutionAsync(cancellationTokenSource.Token);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenUsingMinuteScheduleThenSecondExecutionOccursAtFollowingMinute()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        scheduler.AddJob("*/1 * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(30), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);

        await timeProvider.AdvanceAsync(TimeSpan.FromMinutes(1), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(2, cancellationTokenSource.Token);

        // Assert
        Assert.Equal(2, job.ExecutionCount);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenNextExecutionTimeIsPastThenSchedulerSkipsToFutureOccurrence()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var handle = scheduler.AddJob("*/1 * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), handle.NextExecutionTime);
        Assert.False(job.HasExecuted);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartWhenUsingMinuteScheduleAtExactExecutionTimeThenJobExecutesOnlyOncePerMinute()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        scheduler.AddJob("*/1 * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromMinutes(1), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.Equal(1, job.ExecutionCount);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MisfirePolicyCatchUpWhenTimeAdvancesFiveMinutesThenJobFiresFiveTimes()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        scheduler.AddJob("*/1 * * * *", job, "sample");
        await scheduler.StartAsync();

        // Act: advance 5 minutes — scheduled times 10:08, 10:09, 10:10, 10:11, 10:12 all become due
        await timeProvider.AdvanceAsync(TimeSpan.FromMinutes(5), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(5, cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.Equal(5, job.ExecutionCount);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MisfirePolicySkipWhenTimeAdvancesFiveMinutesThenJobFiresOnceAndNextTimeIsNowBased()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var startTime = new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(startTime);
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        var handle = scheduler.AddJob("*/1 * * * *", job, "sample", MisfirePolicy.Skip);
        await scheduler.StartAsync();

        // Act: advance 5 minutes — Skip policy fires only once and advances next from now
        await timeProvider.AdvanceAsync(TimeSpan.FromMinutes(5), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert: only one execution, not five
        Assert.Equal(1, job.ExecutionCount);

        // Next scheduled time should be based on now (10:12:30), not the missed scheduledTime
        // GetNextOccurrence(10:12:30) = 10:13:00
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 13, 0, TimeSpan.Zero), handle.NextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JobErrorWhenHandlerThrowsThenStopAsyncCompletesNormallyAndSubsequentJobsRun()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007

        var throwingJob = new ThrowingJob();
        var followUpJob = new CountingJob();
        scheduler.AddJob("*/10 * * * * *", throwingJob, "throwing");
        scheduler.AddJob("*/10 * * * * *", followUpJob, "followup");

        scheduler.JobError += static (_, _) => throw new InvalidOperationException("handler error");

        await scheduler.StartAsync();

        // Trigger the jobs
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        await followUpJob.WaitForExecutionsAsync(1, cancellationTokenSource.Token);

        // Act: StopAsync must not throw even though the JobError handler threw
        await scheduler.StopAsync(TestContext.Current.CancellationToken);

        // Assert: the follow-up job ran, confirming the scheduler loop kept running
        Assert.True(followUpJob.ExecutionCount >= 1);
    }

    [Fact]
    public async Task RemoveJobWhenJobIsRemovedAfterDueTimeButBeforeFiringThenPreventsExecution()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var target = new RecordingJob();
        // ReSharper disable once AccessToDisposedClosure
        var trigger = new CallbackJob(() => scheduler.RemoveJob("target"));
        scheduler.AddJob("*/10 * * * * *", trigger, "trigger");
        var targetHandle = scheduler.AddJob("*/10 * * * * *", target, "target");
        await scheduler.StartAsync();

        // Act
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        await trigger.WaitForExecutionAsync(cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert
        Assert.True(targetHandle.IsRemoved);
        Assert.False(target.HasExecuted);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JobErrorWhenJobThrowsNonSchedulerOperationCanceledExceptionThenReportsError()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        scheduler.AddJob("*/10 * * * * *", new CancelingJob(), "sample");

        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);

        // Act
        await scheduler.StartAsync();
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        var error = await errorSource.Task.WaitAsync(cancellationTokenSource.Token);

        // Assert
        Assert.Equal("sample", error.JobName);
        Assert.IsType<OperationCanceledException>(error.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsyncWhenJobIsRunningThenWaitsForItToComplete()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new GateJob(_ => new ValueTask(gate.Task));
        scheduler.AddJob("*/10 * * * * *", job, "sample");
        await scheduler.StartAsync();

        // Act: the job becomes due, starts running and blocks on the gate.
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        await job.Started.WaitAsync(cancellationTokenSource.Token);

        // Begin stopping while the job is still running.
        var stopTask = scheduler.StopAsync(TestContext.Current.CancellationToken);

        // Assert: StopAsync must not complete while the job is still running.
        await Task.Delay(200, cancellationTokenSource.Token);
        Assert.False(stopTask.IsCompleted);
        Assert.False(job.Completed.IsCompleted);

        // Release the job; StopAsync should now complete and the job must have finished.
        gate.SetResult();
        await stopTask.ConfigureAwait(true);
        Assert.True(job.Completed.IsCompleted);
    }

    [Fact]
    public async Task StopAsyncWithTokenWhenDeadlineExceededThenReturnsFalse()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new GateJob(_ => new ValueTask(gate.Task));
        scheduler.AddJob("*/10 * * * * *", job, "sample");
        await scheduler.StartAsync();

        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        await job.Started.WaitAsync(cancellationTokenSource.Token);

        // Act: stop with a deadline that expires while the job is still running
        using var stopCancellation = new CancellationTokenSource();
        var stopTask = scheduler.StopAsync(stopCancellation.Token);
        await stopCancellation.CancelAsync();
        var stopped = await stopTask.ConfigureAwait(true);

        // Assert: waiting was abandoned and reported as incomplete
        Assert.False(stopped);
        Assert.False(job.Completed.IsCompleted);

        // Release the abandoned job so it does not outlive the test
        gate.SetResult();
        await job.Completed.WaitAsync(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task StopAsyncWithTokenWhenJobsCompleteThenReturnsTrue()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        scheduler.AddJob("*/10 * * * * *", new NopJob(), "sample");
        await scheduler.StartAsync();

        // Act
        var stopped = await scheduler.StopAsync(cancellationTokenSource.Token);

        // Assert
        Assert.True(stopped);
    }

    [Fact]
    public async Task MisfirePolicyCatchUpWhenMaxCatchUpIsSetThenLimitsExecutionsAndResumesFromNow()
    {
        // Arrange
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        var handle = scheduler.AddJob("*/1 * * * *", job, "sample", maxCatchUp: 2);
        await scheduler.StartAsync();

        // Act: advance 5 minutes; without a limit this would fire five times
        await timeProvider.AdvanceAsync(TimeSpan.FromMinutes(5), cancellationTokenSource.Token);
        await job.WaitForExecutionsAsync(2, cancellationTokenSource.Token);
        await Task.Delay(50, cancellationTokenSource.Token);

        // Assert: capped at the configured limit, then scheduling resumes from now (10:12:30 -> 10:13:00)
        Assert.Equal(2, job.ExecutionCount);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 13, 0, TimeSpan.Zero), handle.NextExecutionTime);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddJobWhenMaxCatchUpIsNegativeThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler();
#pragma warning restore CA2007

        // Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.AddJob("*/1 * * * *", new NopJob(), "sample", MisfirePolicy.CatchUp, -1));
    }

    private static CancellationTokenSource CreateCancellationTokenSource()
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationTokenSource.CancelAfter(WaitTimeout);
        return cancellationTokenSource;
    }
}
