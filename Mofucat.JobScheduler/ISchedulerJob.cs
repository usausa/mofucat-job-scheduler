namespace Mofucat.JobScheduler;

public interface ISchedulerJob
{
    ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken);
}
