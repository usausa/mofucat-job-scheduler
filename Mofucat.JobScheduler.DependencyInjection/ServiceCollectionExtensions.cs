namespace Mofucat.JobScheduler.DependencyInjection;

using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJobScheduler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddCoreServices(services);
        return services;
    }

    public static IServiceCollection AddJobSchedulerService(this IServiceCollection services, Action<JobSchedulerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        AddCoreServices(services);
        AddHostedServiceServices(services);

        var registry = services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<SchedulerRegistry>()
            .FirstOrDefault();
        if (registry is null)
        {
            registry = new SchedulerRegistry();
            services.AddSingleton(registry);
        }

        var schedulerOptions = new JobSchedulerOptions(services, registry);
        options(schedulerOptions);

        return services;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JobScheduler>();
    }

    private static void AddHostedServiceServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SchedulerHostedService>());
    }
}
