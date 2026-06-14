namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddJobSchedulerServiceWhenCalledThenRegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddJobSchedulerService(static options =>
        {
            options.UseScopedJob<NopJob>("*/1 * * * *");
        });

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, static service => service is SchedulerHostedService);
    }

    [Fact]
    public void AddJobSchedulerWhenCalledThenDoesNotRegisterHostedService()
    {
        var services = new ServiceCollection();

        services.AddJobScheduler();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();

        Assert.DoesNotContain(hostedServices, static service => service is SchedulerHostedService);
    }

    [Fact]
    public void UseJobWithMisfirePolicyWhenRegisteredThenRegistryContainsExpectedPolicy()
    {
        var services = new ServiceCollection();

        services.AddJobSchedulerService(static options =>
        {
            options.UseJob("*/1 * * * *", new Mock.NopJob(), name: "skip-job", misfirePolicy: MisfirePolicy.Skip);
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<SchedulerRegistry>();

        var registration = Assert.Single(registry.Jobs);
        Assert.Equal(MisfirePolicy.Skip, registration.MisfirePolicy);
    }
}
