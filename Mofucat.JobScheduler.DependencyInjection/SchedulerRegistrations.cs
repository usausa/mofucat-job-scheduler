namespace Mofucat.JobScheduler.DependencyInjection;

public sealed class JobRegistration
{
    public string? Name { get; }

    public string CronExpression { get; }

    public Func<IServiceProvider, ISchedulerJob> Factory { get; }

    public MisfirePolicy MisfirePolicy { get; }

    public int MaxCatchUp { get; }

    public TimeZoneInfo? TimeZone { get; }

    public JobRegistration(string? name, string cronExpression, Func<IServiceProvider, ISchedulerJob> factory, MisfirePolicy misfirePolicy, int maxCatchUp = 0, TimeZoneInfo? timeZone = null)
    {
        Name = name;
        CronExpression = cronExpression;
        Factory = factory;
        MisfirePolicy = misfirePolicy;
        MaxCatchUp = maxCatchUp;
        TimeZone = timeZone;
    }
}

#pragma warning disable CA1002
public sealed class SchedulerRegistry
{
    public List<JobRegistration> Jobs { get; } = [];
}
#pragma warning restore CA1002
