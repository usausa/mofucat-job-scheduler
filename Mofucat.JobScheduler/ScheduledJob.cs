namespace Mofucat.JobScheduler;

internal sealed class ScheduledJob
{
    public string Name { get; }

    public CronExpression Cron { get; }

    public ISchedulerJob Job { get; }

    public JobHandle Handle { get; }

    public DateTimeOffset? Next { get; set; }

    public ScheduledJob(string name, CronExpression cron, ISchedulerJob job, JobHandle handle)
    {
        Name = name;
        Cron = cron;
        Job = job;
        Handle = handle;
    }
}
