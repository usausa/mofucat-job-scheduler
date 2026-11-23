namespace Mofucat.JobScheduler.Tests;

using Microsoft.Extensions.Logging;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class SchedulerHostedServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StartAsyncWhenCalledTwiceThenRegistersJobsOnlyOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobSchedulerService(static options =>
        {
            options.UseJob("*/1 * * * *", new NopJob());
        });
#pragma warning disable CA2007
        await using var provider = services.BuildServiceProvider();
#pragma warning restore CA2007
        var hostedService = provider.GetServices<IHostedService>().OfType<SchedulerHostedService>().Single();
        var scheduler = provider.GetRequiredService<JobScheduler>();

        // Act
        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        // Assert: the job is registered once, not once per StartAsync call
        Assert.Single(scheduler.JobNames);

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsyncWhenLoggingIsRegisteredThenWritesToRegisteredLogger()
    {
        var services = new ServiceCollection();
        var sink = new LogSink();
        services.AddSingleton(sink);
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddJobSchedulerService(static options =>
        {
            options.UseJob("*/1 * * * *", new NopJob());
        });
#pragma warning disable CA2007
        await using var provider = services.BuildServiceProvider();
#pragma warning restore CA2007
        var hostedService = provider.GetServices<IHostedService>().OfType<SchedulerHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Contains("Scheduler started.", sink.Messages);
        Assert.Contains("Scheduler stopped.", sink.Messages);
    }

    [Fact]
    public async Task StartAsyncWhenJobHasTimeZoneThenSchedulesInThatTimeZone()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(new DateTimeOffset(2026, 11, 17, 17, 0, 0, TimeSpan.Zero), TestTimeZones.Tokyo));
        services.AddJobSchedulerService(static options =>
        {
            options.UseJob("0 3 * * *", static _ => new NopJob(), "local");
            options.UseJob("0 3 * * *", static _ => new NopJob(), "utc", timeZone: TimeZoneInfo.Utc);
        });
#pragma warning disable CA2007
        await using var provider = services.BuildServiceProvider();
#pragma warning restore CA2007
        var hostedService = provider.GetServices<IHostedService>().OfType<SchedulerHostedService>().Single();
        var scheduler = provider.GetRequiredService<JobScheduler>();

        await hostedService.StartAsync(CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.FromHours(9)), scheduler.FindJob("local")?.NextExecutionTime);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.Zero), scheduler.FindJob("utc")?.NextExecutionTime);

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsyncWhenJobHasConcurrencyPolicyThenSkipsOverlappingExecution()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
        var job = new LongRunningJob();
        var synchronizationJob = new CountingJob();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddJobSchedulerService(options =>
        {
            options.UseJob("*/10 * * * * *", job, "sample", concurrencyPolicy: ConcurrencyPolicy.Skip);
            options.UseJob("*/10 * * * * *", synchronizationJob, "synchronization");
        });
#pragma warning disable CA2007
        await using var provider = services.BuildServiceProvider();
#pragma warning restore CA2007
        var hostedService = provider.GetServices<IHostedService>().OfType<SchedulerHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
        await synchronizationJob.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        await synchronizationJob.WaitForExecutionsAsync(2, cancellationTokenSource.Token);

        Assert.Equal(1, job.ExecutionCount);

        job.Release();
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsyncWhenJobHasTimeoutThenReportsTimeoutException()
    {
        using var cancellationTokenSource = CreateCancellationTokenSource();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero));
        var job = new LongRunningJob();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddJobSchedulerService(options => options.UseJob("0 3 * * *", job, "sample", timeout: TimeSpan.FromSeconds(10)));
#pragma warning disable CA2007
        await using var provider = services.BuildServiceProvider();
#pragma warning restore CA2007
        var hostedService = provider.GetServices<IHostedService>().OfType<SchedulerHostedService>().Single();
        var scheduler = provider.GetRequiredService<JobScheduler>();
        var errorSource = new TaskCompletionSource<JobErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobError += (_, arguments) => errorSource.TrySetResult(arguments);

        await hostedService.StartAsync(CancellationToken.None);
        scheduler.FindJob("sample")?.Trigger();
        await job.WaitForExecutionsAsync(1, cancellationTokenSource.Token);
        await timeProvider.AdvanceAsync(TimeSpan.FromSeconds(10), cancellationTokenSource.Token);
        var error = await errorSource.Task.WaitAsync(cancellationTokenSource.Token);

        Assert.IsType<TimeoutException>(error.Exception);

        await hostedService.StopAsync(CancellationToken.None);
    }

    private static CancellationTokenSource CreateCancellationTokenSource()
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellationTokenSource.CancelAfter(WaitTimeout);
        return cancellationTokenSource;
    }
}
