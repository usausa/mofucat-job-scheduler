namespace Mofucat.JobScheduler.Tests;

public sealed class JobSchedulerOptionsTests
{
    [Fact]
    public void UseJobWhenExpressionIsWhitespaceThenThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var options = new JobSchedulerOptions(services, new SchedulerRegistrations());

        Assert.Throws<ArgumentException>(() => options.UseJob<TestJob>(" "));
    }

#pragma warning disable CA1812
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class TestJob : ISchedulerJob
    {
        public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
#pragma warning restore CA1812
}
