namespace Mofucat.JobScheduler;

using Microsoft.Extensions.DependencyInjection;

internal sealed class ScopedJobAdapter(IServiceProvider rootProvider, Type jobType) : ISchedulerJob
{
    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        await using var scope = rootProvider.CreateAsyncScope();
        var job = (ISchedulerJob)scope.ServiceProvider.GetRequiredService(jobType);
        await job.ExecuteAsync(time, cancellationToken).ConfigureAwait(false);
    }
}
