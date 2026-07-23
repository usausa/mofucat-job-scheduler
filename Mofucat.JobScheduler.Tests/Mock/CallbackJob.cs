namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class CallbackJob : ISchedulerJob
{
    private readonly TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Action onExecute;

    public CallbackJob(Action onExecute)
    {
        this.onExecute = onExecute;
    }

    public bool HasExecuted => completionSource.Task.IsCompleted;

    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        onExecute();
        completionSource.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public async Task WaitForExecutionAsync(CancellationToken cancellationToken)
    {
        await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
