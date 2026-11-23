namespace Mofucat.JobScheduler;

using System.Diagnostics;
using System.Diagnostics.Metrics;

internal static class SchedulerDiagnostics
{
    private const string Name = "Mofucat.JobScheduler";

    private static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter Meter = new(Name);

    private static readonly Histogram<double> DurationHistogram = Meter.CreateHistogram(
        "scheduler.job.duration",
        unit: "s",
        description: "Duration of job executions.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.05, 0.1, 0.5, 1, 5, 10, 30, 60, 300, 600, 1800, 3600] });

    private static readonly Counter<long> SkippedCounter = Meter.CreateCounter<long>(
        "scheduler.job.skipped",
        unit: "{execution}",
        description: "Number of executions skipped by the concurrency policy.");

    public static Activity? StartExecution(string? jobName)
    {
        if (!ActivitySource.HasListeners())
        {
            return null;
        }

#pragma warning disable CA1031
        try
        {
            return ActivitySource.StartActivity(
                "ExecuteJob",
                ActivityKind.Internal,
                parentContext: default,
                tags: jobName is null ? null : [new KeyValuePair<string, object?>("job.name", jobName)]);
        }
        catch
        {
            Activity.Current = null;
            return null;
        }
#pragma warning restore CA1031
    }

    public static void RecordExecution(Activity? activity, string? jobName, TimeSpan duration, Exception? exception)
    {
#pragma warning disable CA1031
        if ((activity is not null) && (exception is not null))
        {
            try
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.AddException(exception);
            }
            catch
            {
                // Ignore
            }
        }

        if (DurationHistogram.Enabled)
        {
            try
            {
                var tags = default(TagList);
                if (jobName is not null)
                {
                    tags.Add("job.name", jobName);
                }

                if (exception is not null)
                {
                    tags.Add("error.type", exception.GetType().FullName);
                }

                DurationHistogram.Record(duration.TotalSeconds, tags);
            }
            catch
            {
                // Ignore
            }
        }
#pragma warning restore CA1031
    }

    public static void StopExecution(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

#pragma warning disable CA1031
        try
        {
            activity.Dispose();
        }
        catch
        {
            // Ignore
        }
#pragma warning restore CA1031
    }

    public static void Skip(string? jobName)
    {
        if (!SkippedCounter.Enabled)
        {
            return;
        }

#pragma warning disable CA1031
        try
        {
            if (jobName is null)
            {
                SkippedCounter.Add(1);
            }
            else
            {
                SkippedCounter.Add(1, new KeyValuePair<string, object?>("job.name", jobName));
            }
        }
        catch
        {
            // Ignore
        }
#pragma warning restore CA1031
    }
}
