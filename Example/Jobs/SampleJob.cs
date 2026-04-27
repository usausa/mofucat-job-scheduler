namespace Example.Jobs;

using Mofucat.JobScheduler;

#pragma warning disable CA1848
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class SampleJob : ISchedulerJob
{
    private readonly ILogger<SampleJob> log;

    public SampleJob(ILogger<SampleJob> log)
    {
        this.log = log;
    }

    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        log.LogInformation("Run at {Time:HH:mm:ss}.", time);

        return ValueTask.CompletedTask;
    }
}
#pragma warning restore CA1848
