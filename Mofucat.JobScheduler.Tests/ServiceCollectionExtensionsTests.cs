namespace Mofucat.JobScheduler.Tests;

#pragma warning disable CA1812
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddJobSchedulerWhenCalledThenRegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddJobScheduler(static options =>
        {
            options.UseJob<TestJob>("*/1 * * * *");
        });

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, static service => service is SchedulerHostedService);
    }

    private sealed class TestJob : ISchedulerJob
    {
        public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
#pragma warning restore CA1812
