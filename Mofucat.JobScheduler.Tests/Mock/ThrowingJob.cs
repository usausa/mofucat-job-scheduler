namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class ThrowingJob : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("job error");
}
