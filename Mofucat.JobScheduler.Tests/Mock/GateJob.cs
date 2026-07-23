namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class GateJob : ISchedulerJob
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Func<CancellationToken, ValueTask> block;

    public GateJob(Func<CancellationToken, ValueTask> block)
    {
        this.block = block;
    }

    public Task Started => started.Task;

    public Task Completed => completed.Task;

    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        started.TrySetResult();
        await block(cancellationToken).ConfigureAwait(false);
        completed.TrySetResult();
    }
}
