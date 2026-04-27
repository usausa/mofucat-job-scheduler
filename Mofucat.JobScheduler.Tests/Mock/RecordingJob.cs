namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class RecordingJob : ISchedulerJob
{
    private readonly TaskCompletionSource<DateTimeOffset> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool HasExecuted => completionSource.Task.IsCompleted;

    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        completionSource.TrySetResult(time);
        return ValueTask.CompletedTask;
    }

    public async Task<DateTimeOffset> WaitForExecutionAsync(CancellationToken cancellationToken)
    {
        var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA2007
        await using var registration = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), cancellationTaskSource);
#pragma warning restore CA2007

        var completedTask = await Task.WhenAny(completionSource.Task, cancellationTaskSource.Task).ConfigureAwait(false);
        if (completedTask != completionSource.Task)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await completionSource.Task.ConfigureAwait(false);
    }
}
