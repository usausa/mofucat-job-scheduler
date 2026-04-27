namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.TestJobs;

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

}
