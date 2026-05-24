namespace Mofucat.JobScheduler.DependencyInjection;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

internal sealed class ScopedJobAdapter : ISchedulerJob
{
    private readonly IServiceProvider rootProvider;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private readonly Type jobType;

    public ScopedJobAdapter(
        IServiceProvider rootProvider,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType)
    {
        this.rootProvider = rootProvider;
        this.jobType = jobType;
    }

    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007
        await using var scope = rootProvider.CreateAsyncScope();
#pragma warning restore CA2007
        var job = (ISchedulerJob)scope.ServiceProvider.GetRequiredService(jobType);
        await job.ExecuteAsync(time, cancellationToken).ConfigureAwait(false);
    }
}
