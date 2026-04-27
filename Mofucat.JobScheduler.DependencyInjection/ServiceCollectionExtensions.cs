namespace Mofucat.JobScheduler.DependencyInjection;

using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds scheduler services and optional job registrations to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The optional scheduler configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddJobScheduler(this IServiceCollection services, Action<JobSchedulerOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JobScheduler>();
        var registrations = GetOrCreateRegistrations(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SchedulerHostedService>());

        if (options is not null)
        {
            var schedulerOptions = new JobSchedulerOptions(services, registrations);
            options(schedulerOptions);
        }

        return services;
    }

    /// <summary>
    /// Adds a scheduled job registration for the specified job type.
    /// </summary>
    /// <typeparam name="T">The job type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="expression">The cron expression.</param>
    /// <param name="name">The optional job name.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddJobSchedulerJob<T>(this IServiceCollection services, string expression, string? name = null)
        where T : class, ISchedulerJob
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddJobScheduler(options => options.UseJob<T>(expression, name));
    }

    private static SchedulerRegistrations GetOrCreateRegistrations(IServiceCollection services)
    {
        if (services
                .FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(SchedulerRegistrations))
                ?.ImplementationInstance is SchedulerRegistrations registration)
        {
            return registration;
        }

        var created = new SchedulerRegistrations();
        services.AddSingleton(created);
        return created;
    }
}
