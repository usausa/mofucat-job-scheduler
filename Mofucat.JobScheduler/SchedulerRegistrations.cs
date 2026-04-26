namespace Mofucat.JobScheduler;

public sealed class JobRegistration(string cronExpression, string? name, Func<IServiceProvider, ISchedulerJob> factory)
{
    public string CronExpression { get; } = cronExpression;

    public string? Name { get; } = name;

    public Func<IServiceProvider, ISchedulerJob> Factory { get; } = factory;
}

public sealed class SchedulerRegistrations
{
    public List<JobRegistration> Jobs { get; } = [];
}
