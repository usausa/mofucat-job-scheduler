namespace Mofucat.JobScheduler;

/// <summary>
/// Represents a handle to a registered job.
/// </summary>
public interface IJobHandle
{
    /// <summary>
    /// Gets the job name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the cron expression.
    /// </summary>
    string CronExpression { get; }

    /// <summary>
    /// Gets a value indicating whether the job has been removed.
    /// </summary>
    bool IsRemoved { get; }

    /// <summary>
    /// Removes the job.
    /// </summary>
    /// <returns><see langword="true" /> when the job was removed.</returns>
    bool Remove();
}
