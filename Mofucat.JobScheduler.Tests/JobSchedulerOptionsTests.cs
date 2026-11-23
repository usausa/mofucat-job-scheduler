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

    [Fact]
    public void UseJobWhenTypeAlreadyRegisteredAsScopedThenThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<NopJob>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddJobSchedulerService(static options => options.UseJob<NopJob>("*/1 * * * *")));

        Assert.Contains("UseScopedJob", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseJobWhenTypeAlreadyRegisteredAsSingletonThenDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<NopJob>();

        services.AddJobSchedulerService(static options => options.UseJob<NopJob>("*/1 * * * *"));

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<NopJob>());
    }

    [Fact]
    public void UseScopedJobWhenNameIsAlreadyRegisteredThenThrowsArgumentException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddJobSchedulerService(static options =>
        {
            options.UseJob("*/1 * * * *", new NopJob(), "sample");
            options.UseScopedJob<NopJob>("*/1 * * * *", "sample");
        }));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void UseJobWhenNameIsRegisteredByAnotherCallThenThrowsArgumentException()
    {
        var services = new ServiceCollection();
        services.AddJobSchedulerService(static options => options.UseJob("*/1 * * * *", static _ => new NopJob(), "sample"));

        var exception = Assert.Throws<ArgumentException>(
            () => services.AddJobSchedulerService(static options => options.UseJob("*/1 * * * *", static _ => new NopJob(), "sample")));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void UseJobWhenNameIsNotSpecifiedThenRegistersEachJob()
    {
        var services = new ServiceCollection();

        services.AddJobSchedulerService(static options =>
        {
            options.UseJob("*/1 * * * *", new NopJob());
            options.UseJob("*/1 * * * *", new NopJob());
        });

        var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetRequiredService<SchedulerRegistry>().Jobs.Count);
    }

    [Fact]
    public void UseJobWhenMaxCatchUpIsNegativeThenThrowsArgumentOutOfRangeException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddJobSchedulerService(static options => options.UseJob<NopJob>("*/1 * * * *", maxCatchUp: -1)));
    }

    [Fact]
    public void UseJobWhenTimeoutIsNotPositiveThenThrowsArgumentOutOfRangeException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddJobSchedulerService(static options => options.UseJob<NopJob>("*/1 * * * *", timeout: TimeSpan.Zero)));

        Assert.Equal("timeout", exception.ParamName);
    }
}
