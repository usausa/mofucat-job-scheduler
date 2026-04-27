namespace Mofucat.JobScheduler;

public sealed class JobErrorEventArgs : EventArgs
{
    public string JobName { get; }

    public Exception Exception { get; }

    public JobErrorEventArgs(string jobName, Exception exception)
    {
        JobName = jobName;
        Exception = exception;
    }
}
