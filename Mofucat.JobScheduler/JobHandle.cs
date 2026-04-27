namespace Mofucat.JobScheduler;

internal sealed class JobHandle : IJobHandle
{
    private readonly JobScheduler scheduler;

    private volatile bool removed;

    public string Name { get; }

    public string CronExpression { get; }

    public bool IsRemoved => removed;

    public JobHandle(JobScheduler scheduler, string name, string cronExpression)
    {
        this.scheduler = scheduler;
        Name = name;
        CronExpression = cronExpression;
    }

    public bool Remove()
    {
        if (removed)
        {
            return false;
        }

        return scheduler.RemoveJob(Name);
    }

    internal void MarkRemoved() => removed = true;
}
