namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Jobs;

public sealed class ServiceCollectionExtensionsTest
{
    [Fact]
    public void AddJobSchedulerWhenCalledThenRegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddJobScheduler(static options =>
        {
            options.UseJob<NopJob>("*/1 * * * *");
        });

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, static service => service is SchedulerHostedService);
    }

    [Fact]
    public void AddJobSchedulerCoreWhenCalledThenDoesNotRegisterHostedService()
    {
        var services = new ServiceCollection();

        services.AddJobSchedulerCore();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, static service => service is SchedulerHostedService);
    }
}
