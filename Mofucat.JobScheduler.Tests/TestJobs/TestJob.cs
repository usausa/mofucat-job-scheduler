namespace Mofucat.JobScheduler.Tests.TestJobs;

public sealed class TestJob : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
