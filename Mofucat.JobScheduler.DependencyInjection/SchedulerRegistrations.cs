namespace Mofucat.JobScheduler.DependencyInjection;

public sealed class JobRegistration
{
    public string? Name { get; }

    public string CronExpression { get; }

    public Func<IServiceProvider, ISchedulerJob> Factory { get; }

    public JobRegistration(string? name, string cronExpression, Func<IServiceProvider, ISchedulerJob> factory)
    {
        Name = name;
        CronExpression = cronExpression;
        Factory = factory;
    }
}

public sealed class SchedulerRegistry
{
    public List<JobRegistration> Jobs { get; } = [];
}
