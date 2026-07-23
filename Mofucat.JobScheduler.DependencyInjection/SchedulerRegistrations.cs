namespace Mofucat.JobScheduler.DependencyInjection;

public sealed class JobRegistration
{
    public string? Name { get; }

    public string CronExpression { get; }

    public Func<IServiceProvider, ISchedulerJob> Factory { get; }

    public MisfirePolicy MisfirePolicy { get; }

    public int MaxCatchUp { get; }

    public JobRegistration(string? name, string cronExpression, Func<IServiceProvider, ISchedulerJob> factory, MisfirePolicy misfirePolicy, int maxCatchUp = 0)
    {
        Name = name;
        CronExpression = cronExpression;
        Factory = factory;
        MisfirePolicy = misfirePolicy;
        MaxCatchUp = maxCatchUp;
    }
}

public sealed class SchedulerRegistry
{
    public List<JobRegistration> Jobs { get; } = [];
}
