namespace Mofucat.JobScheduler;

internal sealed class JobHandle : IJobHandle
{
    private readonly JobScheduler scheduler;

    private readonly ScheduledJob job;

    public string Name => job.Name;

    public string CronExpression { get; }

    public JobState State => job.State;

    public bool IsRunning => job.IsRunning;

    public DateTimeOffset? NextExecutionTime => scheduler.GetNextExecutionTime(job);

    public JobExecutionResult? LastExecution => job.LastExecution;

    public JobHandle(JobScheduler scheduler, ScheduledJob job, string cronExpression)
    {
        this.scheduler = scheduler;
        this.job = job;
        CronExpression = cronExpression;
    }

    public bool Remove() => scheduler.RemoveJob(job);

    public bool Pause() => scheduler.PauseJob(job);

    public bool Resume() => scheduler.ResumeJob(job);

    public bool Trigger() => scheduler.TriggerJob(job);
}
