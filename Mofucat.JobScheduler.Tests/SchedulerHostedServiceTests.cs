namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class SchedulerHostedServiceTests
{
    [Fact]
    public async Task StartAsyncWhenCalledTwiceThenRegistersJobsOnlyOnce()
    {
        // Arrange
        var services = new ServiceCollection();
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
}
