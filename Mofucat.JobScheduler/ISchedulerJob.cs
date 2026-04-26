namespace Mofucat.JobScheduler;

/// <summary>
/// Represents a scheduled job that can be executed by the scheduler.
/// </summary>
public interface ISchedulerJob
{
    /// <summary>
    /// Executes the scheduled job.
    /// </summary>
    /// <param name="time">The scheduled execution time.</param>
    /// <param name="cancellationToken">The cancellation token for the execution.</param>
    ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken);
}
