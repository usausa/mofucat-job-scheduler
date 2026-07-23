namespace Mofucat.JobScheduler.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class JobSchedulerOptions
{
    private readonly IServiceCollection services;

    private readonly SchedulerRegistry registry;

    internal JobSchedulerOptions(IServiceCollection services, SchedulerRegistry registry)
    {
        this.services = services;
        this.registry = registry;
    }

    public void UseJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string expression, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0)
        where T : class, ISchedulerJob
    {
        ValidateCronExpression(expression);

        var existing = services.FirstOrDefault(static descriptor => !descriptor.IsKeyedService && (descriptor.ServiceType == typeof(T)));
        if ((existing is not null) && (existing.Lifetime == ServiceLifetime.Scoped))
        {
            throw new InvalidOperationException($"Job type is already registered as Scoped. UseJob<T>() resolves from the root provider; use UseScopedJob<T>() instead. type=[{typeof(T)}]");
        }

        services.TryAddSingleton<T>();
        registry.Jobs.Add(new JobRegistration(name, expression, static serviceProvider => serviceProvider.GetRequiredService<T>(), misfirePolicy, maxCatchUp));
    }

    public void UseScopedJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string expression, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0)
        where T : class, ISchedulerJob
    {
        ValidateCronExpression(expression);

        services.TryAddScoped<T>();
        registry.Jobs.Add(new JobRegistration(name, expression, static serviceProvider => new ScopedJobAdapter(serviceProvider, typeof(T)), misfirePolicy, maxCatchUp));
    }

    [RequiresDynamicCode("Type-based DI registration requires dynamic code. Use the generic UseScopedJob<T>() overload instead.")]
    [RequiresUnreferencedCode("Type-based DI registration may not be compatible with trimming. Use the generic UseScopedJob<T>() overload instead.")]
    public void UseScopedJob(string expression, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0)
    {
        ValidateCronExpression(expression);

        if (!typeof(ISchedulerJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException($"Type does not implement ISchedulerJob. jobType=[{jobType}]", nameof(jobType));
        }

        services.TryAdd(ServiceDescriptor.Scoped(jobType, jobType));
        registry.Jobs.Add(new JobRegistration(name, expression, serviceProvider => new ScopedJobAdapter(serviceProvider, jobType), misfirePolicy, maxCatchUp));
    }

    public void UseJob(string expression, ISchedulerJob job, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0)
    {
        ValidateCronExpression(expression);

        registry.Jobs.Add(new JobRegistration(name, expression, _ => job, misfirePolicy, maxCatchUp));
    }

    public void UseJob(string expression, Func<IServiceProvider, ISchedulerJob> factory, string? name = null, MisfirePolicy misfirePolicy = MisfirePolicy.CatchUp, int maxCatchUp = 0)
    {
        ValidateCronExpression(expression);

        registry.Jobs.Add(new JobRegistration(name, expression, factory, misfirePolicy, maxCatchUp));
    }

    private static void ValidateCronExpression(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _ = CronExpression.Parse(expression);
    }
}
