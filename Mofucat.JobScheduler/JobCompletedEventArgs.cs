namespace Mofucat.JobScheduler;

public sealed class JobCompletedEventArgs : EventArgs
{
    public string JobName { get; }

    public DateTimeOffset Time { get; }

    public TimeSpan Duration { get; }

    public Exception? Exception { get; }

    public JobCompletedEventArgs(string jobName, DateTimeOffset time, TimeSpan duration, Exception? exception)
    {
        JobName = jobName;
        Time = time;
        Duration = duration;
        Exception = exception;
    }
}
