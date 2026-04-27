namespace Mofucat.JobScheduler;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Provides configuration for scheduled jobs.
/// </summary>
public sealed class JobSchedulerOptions
{
    private readonly SchedulerRegistrations registrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobSchedulerOptions"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registrations">The scheduler registrations.</param>
    public JobSchedulerOptions(IServiceCollection services, SchedulerRegistrations registrations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);

        Services = services;
        this.registrations = registrations;
    }

    /// <summary>
    /// Gets the configured service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a scheduled job using a scoped implementation.
    /// </summary>
    /// <typeparam name="T">The job type.</typeparam>
    /// <param name="expression">The cron expression.</param>
    /// <param name="name">The optional job name.</param>
    public void UseJob<T>(string expression, string? name = null)
        where T : class, ISchedulerJob
    {
        ValidateCronExpression(expression);
        Services.TryAddScoped<T>();
        registrations.Jobs.Add(new JobRegistration(expression, name, static serviceProvider => new ScopedJobAdapter(serviceProvider, typeof(T))));
    }

    /// <summary>
    /// Registers a scheduled job using the specified runtime type.
    /// </summary>
    /// <param name="expression">The cron expression.</param>
    /// <param name="jobType">The job type.</param>
    /// <param name="name">The optional job name.</param>
    public void UseJob(string expression, Type jobType, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(jobType);

        if (!typeof(ISchedulerJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException($"Type {jobType} does not implement ISchedulerJob.", nameof(jobType));
        }

        Services.TryAdd(ServiceDescriptor.Scoped(jobType, jobType));
        registrations.Jobs.Add(new JobRegistration(expression, name, serviceProvider => new ScopedJobAdapter(serviceProvider, jobType)));
    }

    /// <summary>
    /// Registers a scheduled job instance.
    /// </summary>
    /// <param name="expression">The cron expression.</param>
    /// <param name="job">The job instance.</param>
    /// <param name="name">The optional job name.</param>
    public void UseJob(string expression, ISchedulerJob job, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(job);
        registrations.Jobs.Add(new JobRegistration(expression, name, _ => job));
    }

    /// <summary>
    /// Registers a scheduled job factory.
    /// </summary>
    /// <param name="expression">The cron expression.</param>
    /// <param name="factory">The factory used to create the job.</param>
    /// <param name="name">The optional job name.</param>
    public void UseJob(string expression, Func<IServiceProvider, ISchedulerJob> factory, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(factory);
        registrations.Jobs.Add(new JobRegistration(expression, name, factory));
    }

    private static void ValidateCronExpression(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _ = CronExpression.Parse(expression);
    }
}
