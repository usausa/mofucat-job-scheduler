namespace Mofucat.JobScheduler.Tests.Mock;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class NopJob : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
