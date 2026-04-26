namespace Mofucat.JobScheduler;

internal sealed class JobHandle(JobScheduler scheduler, string name, string cronExpression) : IJobHandle
{
    private volatile bool removed;

    public string Name { get; } = name;

    public string CronExpression { get; } = cronExpression;

    public bool IsRemoved => removed;

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
