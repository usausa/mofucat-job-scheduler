namespace Mofucat.JobScheduler;

/// <summary>
/// Provides information about a job execution failure.
/// </summary>
public sealed class JobErrorEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JobErrorEventArgs"/> class.
    /// </summary>
    /// <param name="jobName">The job name.</param>
    /// <param name="exception">The exception that occurred.</param>
    public JobErrorEventArgs(string jobName, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(exception);

        JobName = jobName;
        Exception = exception;
    }

    /// <summary>
    /// Gets the job name.
    /// </summary>
    public string JobName { get; }

    /// <summary>
    /// Gets the exception that occurred.
    /// </summary>
    public Exception Exception { get; }
}
