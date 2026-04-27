namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class JobSchedulerOptionsTests
{
    [Fact]
    public void UseScopedJobWhenExpressionIsWhitespaceThenThrowsArgumentException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddJobSchedulerService(static options => options.UseScopedJob<NopJob>(" ")));
    }

    [Fact]
    public void UseJobWhenCalledThenRegistersJobType()
    {
        var services = new ServiceCollection();

        services.AddJobSchedulerService(static options => options.UseJob<NopJob>("*/1 * * * *"));

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<NopJob>());
    }
}
