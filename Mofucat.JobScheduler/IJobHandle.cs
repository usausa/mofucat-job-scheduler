namespace Mofucat.JobScheduler;

public interface IJobHandle
{
    string Name { get; }

    string CronExpression { get; }

    bool IsRemoved { get; }

    DateTimeOffset? NextExecutionTime { get; }

    bool Remove();
}
