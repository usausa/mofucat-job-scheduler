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
        var scope = rootProvider.CreateAsyncScope();
        try
        {
            var job = (ISchedulerJob)scope.ServiceProvider.GetRequiredService(jobType);
            await job.ExecuteAsync(time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            await DisposeScopeAsync(scope).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        await scope.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task DisposeScopeAsync(AsyncServiceScope scope) => await scope.DisposeAsync().ConfigureAwait(false);
}
