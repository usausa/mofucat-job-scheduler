namespace Mofucat.JobScheduler;

internal sealed class ScheduledJob
{
    public ScheduledJob(string name, CronExpression cron, ISchedulerJob job, JobHandle handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cron);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(handle);

        Name = name;
        Cron = cron;
        Job = job;
        Handle = handle;
    }

    public string Name { get; }

    public CronExpression Cron { get; }

    public ISchedulerJob Job { get; }

    public JobHandle Handle { get; }

    public DateTimeOffset? Next { get; set; }
}
