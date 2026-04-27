namespace Mofucat.JobScheduler.DependencyInjection;

using System.Runtime;

using Microsoft.Extensions.Logging;

internal static partial class Log
{
    // Scheduler

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler started.")]
    public static partial void InfoSchedulerStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler stopped.")]
    public static partial void InfoSchedulerStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduler job failed. jobName=[{jobName}]")]
    public static partial void ErrorSchedulerJobFailed(this ILogger logger, Exception exception, string? jobName);
}
