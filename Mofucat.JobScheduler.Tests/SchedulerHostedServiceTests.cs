namespace Mofucat.JobScheduler.Tests;

using Microsoft.Extensions.Logging;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class SchedulerHostedServiceTests
{
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
}
