namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class SyncDisposableJob(DisposableJobState state) : ISchedulerJob, IDisposable
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) =>
        state.ExecuteException is null ? ValueTask.CompletedTask : ValueTask.FromException(state.ExecuteException);

    public void Dispose()
    {
        state.IsDisposed = true;
        state.ThrowDisposeException();
    }
}
