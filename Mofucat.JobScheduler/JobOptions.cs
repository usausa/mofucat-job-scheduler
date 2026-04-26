namespace Mofucat.JobScheduler;

internal sealed class JobOptions(string expression, TimeZoneInfo timeZoneInfo, ISchedulerJob job)
{
    public string Expression { get; } = expression;

    public TimeZoneInfo TimeZoneInfo { get; } = timeZoneInfo;

    public ISchedulerJob Job { get; } = job;
}
