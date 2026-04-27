namespace Mofucat.JobScheduler.DependencyInjection;

public sealed class JobRegistration
{
    public JobRegistration(string cronExpression, string? name, Func<IServiceProvider, ISchedulerJob> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentNullException.ThrowIfNull(factory);

        CronExpression = cronExpression;
        Name = name;
        Factory = factory;
    }

    public string CronExpression { get; }

    public string? Name { get; }

    public Func<IServiceProvider, ISchedulerJob> Factory { get; }
}

public sealed class SchedulerRegistrations
{
    public List<JobRegistration> Jobs { get; } = [];
}
