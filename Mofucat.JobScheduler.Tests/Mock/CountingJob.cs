namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class CountingJob : ISchedulerJob
{
    private readonly Lock sync = new();

    public int ExecutionCount { get; private set; }

    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            ExecutionCount++;
        }

        return ValueTask.CompletedTask;
    }

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
