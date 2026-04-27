namespace Example.Jobs;

using Mofucat.JobScheduler;

#pragma warning disable CA1848
public sealed class SampleJob(ILogger<SampleJob> log) : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        log.LogInformation("Run at {Time:HH:mm:ss}.", time);

        return ValueTask.CompletedTask;
    }
}
#pragma warning restore CA1848
