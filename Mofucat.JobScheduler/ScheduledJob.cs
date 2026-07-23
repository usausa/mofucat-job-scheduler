namespace Mofucat.JobScheduler;

internal sealed class ScheduledJob
{
    public string Name { get; }

    public CronExpression Cron { get; }

    public ISchedulerJob Job { get; }

    public JobHandle Handle { get; }

    public MisfirePolicy MisfirePolicy { get; }

    public int MaxCatchUp { get; }

    public DateTimeOffset? Next { get; set; }

    public int CatchUpCount { get; set; }

    public ScheduledJob(string name, CronExpression cron, ISchedulerJob job, JobHandle handle, MisfirePolicy misfirePolicy, int maxCatchUp)
    {
        Name = name;
        Cron = cron;
        Job = job;
        Handle = handle;
        MisfirePolicy = misfirePolicy;
        MaxCatchUp = maxCatchUp;
    }
}
