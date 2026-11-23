namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class DisposableJob(DisposableJobState state) : ISchedulerJob, IAsyncDisposable
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) =>
        state.ExecuteException is null ? ValueTask.CompletedTask : ValueTask.FromException(state.ExecuteException);

    public ValueTask DisposeAsync()
    {
        state.IsDisposed = true;
        return state.DisposeException is null ? ValueTask.CompletedTask : ValueTask.FromException(state.DisposeException);
    }
}
