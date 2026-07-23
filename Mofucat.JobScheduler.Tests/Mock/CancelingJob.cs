namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class CancelingJob : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) =>
        throw new OperationCanceledException("Job internal cancellation");
}
