namespace Mofucat.JobScheduler;

internal sealed class ScheduledJob(string name, CronExpression cron, ISchedulerJob job, JobHandle handle)
{
    public string Name { get; } = name;

    public CronExpression Cron { get; } = cron;

    public ISchedulerJob Job { get; } = job;

    public JobHandle Handle { get; } = handle;

    public DateTimeOffset? Next { get; set; }
}
