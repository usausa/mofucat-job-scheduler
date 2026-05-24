namespace Mofucat.JobScheduler.DependencyInjection;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class JobSchedulerOptions
{
    private readonly IServiceCollection services;

    private readonly SchedulerRegistry registry;

    internal JobSchedulerOptions(IServiceCollection services, SchedulerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);

        this.services = services;
        this.registry = registry;
    }

    public void UseJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string expression, string? name = null)
        where T : class, ISchedulerJob
    {
        ValidateCronExpression(expression);
        services.TryAddSingleton<T>();
        registry.Jobs.Add(new JobRegistration(name, expression, static serviceProvider => serviceProvider.GetRequiredService<T>()));
    }

    public void UseScopedJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string expression, string? name = null)
        where T : class, ISchedulerJob
    {
        ValidateCronExpression(expression);
        services.TryAddScoped<T>();
        registry.Jobs.Add(new JobRegistration(name, expression, static serviceProvider => new ScopedJobAdapter(serviceProvider, typeof(T))));
    }

    [RequiresDynamicCode("Type-based DI registration requires dynamic code. Use the generic UseScopedJob<T>() overload instead.")]
    [RequiresUnreferencedCode("Type-based DI registration may not be compatible with trimming. Use the generic UseScopedJob<T>() overload instead.")]
    public void UseScopedJob(string expression, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(jobType);

        if (!typeof(ISchedulerJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException($"Type does not implement ISchedulerJob. jobType=[{jobType}]", nameof(jobType));
        }

        services.TryAdd(ServiceDescriptor.Scoped(jobType, jobType));
        registry.Jobs.Add(new JobRegistration(name, expression, serviceProvider => new ScopedJobAdapter(serviceProvider, jobType)));
    }

    public void UseJob(string expression, ISchedulerJob job, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(job);

        registry.Jobs.Add(new JobRegistration(name, expression, _ => job));
    }

    public void UseJob(string expression, Func<IServiceProvider, ISchedulerJob> factory, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(factory);

        registry.Jobs.Add(new JobRegistration(name, expression, factory));
    }

    private static void ValidateCronExpression(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _ = CronExpression.Parse(expression);
    }
}
