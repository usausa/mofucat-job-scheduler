namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class JobExecutionTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task TimeoutWhenExecutionExceedsTimeoutThenReportsTimeoutException()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily", timeout: TimeSpan.FromSeconds(10));
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        var error = await errorSource.Task.WaitAsync(cancellationTokenSource.Token);

        var exception = Assert.IsType<TimeoutException>(error.Exception);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.Same(exception, handle.LastExecution?.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TimeoutWhenExecutionCompletesInTimeThenReportsSuccess()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new NopJob(), "daily", timeout: TimeSpan.FromSeconds(10));
        var completedSource = new TaskCompletionSource<JobCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobCompleted += (_, arguments) => completedSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        var completed = await completedSource.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Null(completed.Exception);
        Assert.Null(handle.LastExecution?.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TimeoutWhenSchedulerStopsDuringExecutionThenDoesNotReportError()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily", timeout: TimeSpan.FromSeconds(10));
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedSource = new TaskCompletionSource<JobCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);
        scheduler.JobCompleted += (_, arguments) => completedSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        var stopped = await scheduler.StopAsync(cancellationTokenSource.Token);
        var completed = await completedSource.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.True(stopped);
        Assert.False(errorSource.Task.IsCompleted);
        Assert.Null(completed.Exception);
        Assert.Null(handle.LastExecution?.Exception);
    }

    [Fact]
    public async Task TimeoutWhenSchedulerStopsAfterTimeoutElapsedThenDoesNotReportError()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new GateJob(async cancellationToken =>
        {
            await gate.Task.ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
        });
        var handle = scheduler.AddJob("0 3 * * *", job, "daily", timeout: TimeSpan.FromSeconds(10));
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.Started.WaitAsync(cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        var stopTask = scheduler.StopAsync(cancellationTokenSource.Token);
        gate.SetResult();
        var stopped = await stopTask.ConfigureAwait(true);

        Assert.True(stopped);
        Assert.False(job.Completed.IsCompleted);
        Assert.False(errorSource.Task.IsCompleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4_320_000)]
    public async Task AddJobWhenTimeoutIsOutOfRangeThenThrowsArgumentOutOfRangeException(int seconds)
    {
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler();
#pragma warning restore CA2007

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.AddJob("*/1 * * * *", new NopJob(), "sample", timeout: TimeSpan.FromSeconds(seconds)));

        Assert.Equal("timeout", exception.ParamName);
    }

    [Fact]
    public async Task JobCompletedWhenExecutionSucceedsThenReportsTimeAndDuration()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily");
        var completedSource = new TaskCompletionSource<JobCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobCompleted += (_, arguments) => completedSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
        job.Release();
        var completed = await completedSource.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Equal("daily", completed.JobName);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero), completed.Time);
        Assert.Equal(TimeSpan.FromSeconds(2), completed.Duration);
        Assert.Null(completed.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JobCompletedWhenExecutionFailsThenReportsSameExceptionAsJobError()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), "daily");
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedSource = new TaskCompletionSource<JobCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);
        scheduler.JobCompleted += (_, arguments) => completedSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        var error = await errorSource.Task.WaitAsync(cancellationTokenSource.Token);
        var completed = await completedSource.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Same(error.Exception, completed.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JobCompletedWhenHandlerThrowsThenSchedulerKeepsRunning()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily");
        scheduler.JobCompleted += static (_, _) => throw new InvalidOperationException("handler error");
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        handle.Trigger();
        await job.WaitForExecutionsAsync(2, cancellationTokenSource.Token);

        Assert.True(await scheduler.StopAsync(cancellationTokenSource.Token));
    }

    [Fact]
    public async Task JobCompletedWhenJobErrorHandlerThrowsThenIsStillRaised()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), "daily");
        var completedSource = new TaskCompletionSource<JobCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += static (_, _) => throw new InvalidOperationException("handler error");
        scheduler.JobCompleted += (_, arguments) => completedSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        var completed = await completedSource.Task.WaitAsync(cancellationTokenSource.Token);

        var exception = Assert.IsType<InvalidOperationException>(completed.Exception);
        Assert.Equal("job error", exception.Message);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrencyPolicySkipWhenJobCompletedHandlerIsRunningThenSkipsOccurrence()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var synchronizationJob = new CountingJob();
        var handle = scheduler.AddJob("0 3 * * *", job, "daily", concurrencyPolicy: ConcurrencyPolicy.Skip);
        var synchronizationHandle = scheduler.AddJob("0 3 * * *", synchronizationJob, "synchronization");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobCompleted += (_, arguments) =>
        {
            if ((arguments.JobName == "daily") && entered.TrySetResult())
            {
                SpinWait.SpinUntil(() => release.Task.IsCompleted);
            }
        };
        await scheduler.StartAsync();

        try
        {
            handle.Trigger();
            await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
            synchronizationHandle.Trigger();
            await synchronizationJob.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
            job.Release();
            await entered.Task.WaitAsync(cancellationTokenSource.Token);
            handle.Trigger();
            synchronizationHandle.Trigger();
            await synchronizationJob.WaitForExecutionsAsync(2, cancellationTokenSource.Token);

            Assert.Equal(1, job.ExecutionCount);
        }
        finally
        {
            release.TrySetResult();
        }

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    private static CancellationTokenSource CreateCancellationTokenSource()
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationTokenSource.CancelAfter(WaitTimeout);
        return cancellationTokenSource;
    }
}
