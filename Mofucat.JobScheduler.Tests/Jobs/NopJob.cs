namespace Mofucat.JobScheduler.Tests.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class NopJob : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
