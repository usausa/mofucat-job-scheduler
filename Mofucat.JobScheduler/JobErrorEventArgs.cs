namespace Mofucat.JobScheduler;

/// <summary>
/// Provides information about a job execution failure.
/// </summary>
public sealed class JobErrorEventArgs(string jobName, Exception exception) : EventArgs
{
    /// <summary>
    /// Gets the job name.
    /// </summary>
    public string JobName { get; } = jobName;

    /// <summary>
    /// Gets the exception that occurred.
    /// </summary>
    public Exception Exception { get; } = exception;
}
