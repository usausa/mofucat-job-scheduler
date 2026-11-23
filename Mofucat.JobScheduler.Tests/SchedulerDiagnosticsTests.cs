namespace Mofucat.JobScheduler.Tests;

using System.Diagnostics;
using System.Diagnostics.Metrics;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class SchedulerDiagnosticsTests
{
    private const string SourceName = "Mofucat.JobScheduler";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ExecuteWhenActivityListenerIsRegisteredThenRunsJobInRootActivity()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var jobName = CreateJobName();
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateActivityListener(jobName, stopped);
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var current = new StrongBox<Activity?>();
        var handle = scheduler.AddJob("0 3 * * *", new CallbackJob(() => current.Value = Activity.Current), jobName);
        using (var parent = new Activity("parent"))
        {
            parent.Start();
            await scheduler.StartAsync();
        }

        handle.Trigger();
        var activity = await stopped.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Equal("ExecuteJob", activity.OperationName);
        Assert.Null(activity.ParentId);
        Assert.Same(activity, current.Value);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWhenJobFailsThenActivityRecordsError()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var jobName = CreateJobName();
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateActivityListener(jobName, stopped);
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), jobName);
        await scheduler.StartAsync();

        handle.Trigger();
        var activity = await stopped.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains(activity.Events, static activityEvent => activityEvent.Name == "exception");

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWhenMeterListenerIsRegisteredThenRecordsDurationAndErrorType()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var successName = CreateJobName();
        var failureName = CreateJobName();
        var success = new TaskCompletionSource<(double Value, string? ErrorType)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource<(double Value, string? ErrorType)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateMeterListener();
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var jobName = FindTag(tags, "job.name");
            if (jobName == successName)
            {
                success.TrySetResult((value, FindTag(tags, "error.type")));
            }
            else if (jobName == failureName)
            {
                failure.TrySetResult((value, FindTag(tags, "error.type")));
            }
        });
        listener.Start();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var successHandle = scheduler.AddJob("0 3 * * *", job, successName);
        var failureHandle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), failureName);
        await scheduler.StartAsync();

        successHandle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
        job.Release();
        failureHandle.Trigger();
        var (successValue, successErrorType) = await success.Task.WaitAsync(cancellationTokenSource.Token);
        var (_, failureErrorType) = await failure.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Equal(2d, successValue);
        Assert.Null(successErrorType);
        Assert.Equal("System.InvalidOperationException", failureErrorType);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SkipWhenMeterListenerIsRegisteredThenCountsSkippedExecution()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var jobName = CreateJobName();
        var skipped = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateMeterListener();
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (FindTag(tags, "job.name") == jobName)
            {
                skipped.TrySetResult(value);
            }
        });
        listener.Start();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new LongRunningJob();
        var handle = scheduler.AddJob("0 3 * * *", job, jobName, concurrencyPolicy: ConcurrencyPolicy.Skip);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        handle.Trigger();
        var value = await skipped.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Equal(1, value);
        Assert.Equal(1, job.ExecutionCount);

        job.Release();
        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWhenActivityListenerThrowsThenJobStillRunsWithoutError()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        using var listener = CreateActivityListener(
            static _ => throw new InvalidOperationException("listener error"),
            static _ => throw new InvalidOperationException("listener error"));
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var job = new CountingJob();
        var handle = scheduler.AddJob("0 3 * * *", job, CreateJobName(), concurrencyPolicy: ConcurrencyPolicy.Skip);
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        handle.Trigger();
        await job.WaitForExecutionsAsync(2, cancellationTokenSource.Token);
        await WaitUntilAsync(() => !handle.IsRunning, cancellationTokenSource.Token);

        Assert.False(errorSource.Task.IsCompleted);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWhenActivityStoppedThrowsThenJobCompletedIsRaised()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        using var listener = CreateActivityListener(null, static _ => throw new InvalidOperationException("listener error"));
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), CreateJobName());
        var completedSource = new TaskCompletionSource<JobCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobCompleted += (_, arguments) => completedSource.TrySetResult(arguments);
        await scheduler.StartAsync();

        handle.Trigger();
        var completed = await completedSource.Task.WaitAsync(cancellationTokenSource.Token);
        await WaitUntilAsync(() => !handle.IsRunning, cancellationTokenSource.Token);

        Assert.IsType<InvalidOperationException>(completed.Exception);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWhenNoListenerIsRegisteredThenJobDoesNotInheritCallerActivity()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var traceId = new StrongBox<ActivityTraceId>();
        var job = new CallbackJob(() => traceId.Value = Activity.Current?.TraceId ?? default);
        var handle = scheduler.AddJob("0 3 * * *", job, CreateJobName());
        ActivityTraceId parentTraceId;
        using (var parent = new Activity("parent"))
        {
            parent.Start();
            parentTraceId = parent.TraceId;
            await scheduler.StartAsync();
        }

        handle.Trigger();
        await job.WaitForExecutionAsync(cancellationTokenSource.Token);

        Assert.NotEqual(parentTraceId, traceId.Value);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JobErrorWhenActivityListenerIsRegisteredThenHandlerRunsInJobActivity()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var jobName = CreateJobName();
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateActivityListener(jobName, stopped);
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new ThrowingJob(), jobName);
        var inHandler = new StrongBox<Activity?>();
        scheduler.JobError += (_, _) => inHandler.Value = Activity.Current;
        await scheduler.StartAsync();

        handle.Trigger();
        var activity = await stopped.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Same(activity, inHandler.Value);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteWhenSamplingThenJobNameIsAvailable()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var jobName = CreateJobName();
        var sampled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = CreateActivityListener(null, null, (ref options) =>
        {
            if (options.Tags?.Any(tag => (tag.Key == "job.name") && Equals(tag.Value, jobName)) == true)
            {
                sampled.TrySetResult();
            }

            return ActivitySamplingResult.AllDataAndRecorded;
        });
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var handle = scheduler.AddJob("0 3 * * *", new NopJob(), jobName);
        await scheduler.StartAsync();

        handle.Trigger();
        await sampled.Task.WaitAsync(cancellationTokenSource.Token);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SkipWhenMeterListenerThrowsThenSchedulerKeepsRunning()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        using var listener = CreateMeterListener();
        listener.SetMeasurementEventCallback<long>(static (_, _, _, _) => throw new InvalidOperationException("listener error"));
        listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => throw new InvalidOperationException("listener error"));
        listener.Start();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var held = new LongRunningJob();
        var other = new CountingJob();
        var otherName = CreateJobName();
        var heldHandle = scheduler.AddJob("0 3 * * *", held, CreateJobName(), concurrencyPolicy: ConcurrencyPolicy.Skip);
        var otherHandle = scheduler.AddJob("0 3 * * *", other, otherName);
        var completedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobCompleted += (_, arguments) =>
        {
            if (arguments.JobName == otherName)
            {
                completedSource.TrySetResult();
            }
        };
        await scheduler.StartAsync();

        heldHandle.Trigger();
        await held.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        heldHandle.Trigger();
        otherHandle.Trigger();
        await other.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await completedSource.Task.WaitAsync(cancellationTokenSource.Token);
        held.Release();

        Assert.True(await scheduler.StopAsync(cancellationTokenSource.Token));
        Assert.Equal(1, held.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteWhenJobHasNoNameThenOmitsJobNameTag()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var current = new StrongBox<Activity?>();
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorded = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var activityListener = CreateActivityListener(null, activity =>
        {
            if (ReferenceEquals(activity, current.Value))
            {
                stopped.TrySetResult(activity);
            }
        });
        using var meterListener = CreateMeterListener();
        meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            if (Math.Abs(value - 3.25) < 1e-9)
            {
                recorded.TrySetResult(FindTag(tags, "job.name"));
            }
        });
        meterListener.Start();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
#pragma warning disable CA2007
        await using var scheduler = new JobScheduler(timeProvider);
#pragma warning restore CA2007
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new GateJob(async _ =>
        {
            current.Value = Activity.Current;
            await gate.Task.ConfigureAwait(true);
        });
        var handle = scheduler.AddJob("0 3 * * *", job);
        await scheduler.StartAsync();

        handle.Trigger();
        await job.Started.WaitAsync(cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(3.25), cancellationTokenSource.Token);
        gate.SetResult();
        var activity = await stopped.Task.WaitAsync(cancellationTokenSource.Token);
        var jobNameTag = await recorded.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.Null(activity.GetTagItem("job.name"));
        Assert.Null(jobNameTag);

        await scheduler.StopAsync(TestContext.Current.CancellationToken);
    }

    private static string CreateJobName() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private static ActivityListener CreateActivityListener(string jobName, TaskCompletionSource<Activity> stopped) =>
        CreateActivityListener(null, activity =>
        {
            if (Equals(activity.GetTagItem("job.name"), jobName))
            {
                stopped.TrySetResult(activity);
            }
        });

    private static ActivityListener CreateActivityListener(Action<Activity>? activityStarted, Action<Activity>? activityStopped, SampleActivity<ActivityContext>? sample = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == SourceName,
            Sample = sample ?? (static (ref _) => ActivitySamplingResult.AllDataAndRecorded),
            ActivityStarted = activityStarted,
            ActivityStopped = activityStopped
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MeterListener CreateMeterListener() =>
        new()
        {
            InstrumentPublished = static (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == SourceName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

    private static string? FindTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value as string;
            }
        }

        return null;
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
