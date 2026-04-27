namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Jobs;

public sealed class JobSchedulerOptionsTest
{
    [Fact]
    public void UseJobWhenExpressionIsWhitespaceThenThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var options = new JobSchedulerOptions(services, new SchedulerRegistrations());

        Assert.Throws<ArgumentException>(() => options.UseJob<NopJob>(" "));
    }
}
