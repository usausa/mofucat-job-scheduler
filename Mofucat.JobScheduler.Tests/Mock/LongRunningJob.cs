namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class LongRunningJob : ISchedulerJob
{
    private readonly Lock sync = new();

    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ExecutionCount { get; private set; }

    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            ExecutionCount++;
        }

        await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release() => release.TrySetResult();

    public async Task WaitForExecutionsAsync(int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (sync)
            {
                if (ExecutionCount >= count)
                {
                    return;
                }
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }
}
