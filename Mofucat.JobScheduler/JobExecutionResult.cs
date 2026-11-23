namespace Mofucat.JobScheduler;

public readonly record struct JobExecutionResult(
    DateTimeOffset Time,
    TimeSpan Duration,
    Exception? Exception);
