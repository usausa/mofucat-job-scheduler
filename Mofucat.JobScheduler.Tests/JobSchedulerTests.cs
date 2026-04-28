namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class JobSchedulerTests
{
    [Fact]
    public async Task StartWhenJobRunsThenUsesTimeProviderForExecutionTime()
    {
        // Arrange
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        scheduler.AddJob("*/10 * * * * *", job, "sample");

        // Act
        await scheduler.StartAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var nextRun = await job.WaitForExecutionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task RemoveAllJobsWhenSchedulerIsRunningThenRemovesJobsAndPreventsExecution()
    {
        // Arrange
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
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
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
        // Arrange
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        await scheduler.StartAsync();

        var job = new RecordingJob();

        // Act
        var handle = scheduler.AddJob("*/10 * * * * *", job, "dynamic");
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var nextRun = await job.WaitForExecutionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("dynamic", handle.Name);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), nextRun);

        await scheduler.StopAsync();
    }

    [Fact]
    public async Task RemoveJobWhenJobIsRemovedBeforeDueTimeThenReturnsRemovedHandleAndPreventsExecution()
    {
        // Arrange
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new RecordingJob();
        var handle = scheduler.AddJob("*/10 * * * * *", job, "dynamic");
        await scheduler.StartAsync();

        // Act
        var removed = handle.Remove();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removed);
        Assert.True(handle.IsRemoved);
        Assert.False(job.HasExecuted);

        await scheduler.StopAsync();
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

        await scheduler.StopAsync();
    }
}
