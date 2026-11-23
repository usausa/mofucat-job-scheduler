namespace Mofucat.JobScheduler;

public interface IJobHandle
{
    string Name { get; }

    string CronExpression { get; }

    bool IsRemoved { get; }

    bool IsPaused { get; }

    bool IsRunning { get; }

    DateTimeOffset? NextExecutionTime { get; }

    JobExecutionResult? LastExecution { get; }

    bool Remove();

    bool Pause();

#pragma warning disable CA1716
    bool Resume();
#pragma warning restore CA1716

    bool Trigger();
}
