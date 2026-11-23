# Mofucat.JobScheduler

[![NuGet](https://img.shields.io/nuget/v/Mofucat.JobScheduler.svg)](https://www.nuget.org/packages/Mofucat.JobScheduler)
[![NuGet](https://img.shields.io/nuget/v/Mofucat.JobScheduler.DependencyInjection.svg)](https://www.nuget.org/packages/Mofucat.JobScheduler.DependencyInjection)

Lightweight cron-based job scheduler library.

## Basic

Create a scheduler, register jobs, and start the execution loop:

```csharp
using Mofucat.JobScheduler;

await using var scheduler = new JobScheduler();

scheduler.AddJob("*/10 * * * * *", new SampleJob(), "sample");

await scheduler.StartAsync();

// ...

await scheduler.StopAsync();

internal sealed class SampleJob : ISchedulerJob
{
    public ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Run at {time:HH:mm:ss}.");
        return ValueTask.CompletedTask;
    }
}
```

`ExecuteAsync` runs on the scheduler loop until its first `await` that does not complete synchronously, so heavy synchronous work there delays other jobs. Start such a job with `await Task.Yield()` to run the rest on the thread pool.

## Dependency injection

Use `Mofucat.JobScheduler.DependencyInjection` to register the scheduler as a hosted service:

```csharp
using Mofucat.JobScheduler.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddJobSchedulerService(static options =>
{
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "sample");
});

builder.Build().Run();
```

Use a factory to create the job from the service provider. The factory is called once when the hosted service starts:

```csharp
builder.Services.AddJobSchedulerService(static options =>
{
    options.UseJob("0 3 * * *", static provider => ActivatorUtilities.CreateInstance<SampleJob>(provider), name: "daily");
});
```

## Time zone

Cron expressions are evaluated in the local time zone (`TimeProvider.LocalTimeZone`, which is `TimeZoneInfo.Local` for `TimeProvider.System`). Pass `timeZone` to evaluate a job in another time zone:

```csharp
// 03:00 local time
scheduler.AddJob("0 3 * * *", new SampleJob(), "local");

// 03:00 UTC
scheduler.AddJob("0 3 * * *", new SampleJob(), "utc", timeZone: TimeZoneInfo.Utc);
```

```csharp
builder.Services.AddJobSchedulerService(static options =>
{
    options.UseScopedJob<SampleJob>("0 3 * * *", name: "utc", timeZone: TimeZoneInfo.Utc);
});
```

`IJobHandle.NextExecutionTime` and the time passed to `ExecuteAsync` use the offset of the job's time zone.

Daylight saving time transitions are handled as in cron on Linux. An expression whose minute or hour field starts with `*` is a wildcard job; any other expression is a fixed-time job.

| Case | Fixed-time job | Wildcard job |
|---|---|---|
| Skipped time (clock moves forward) | Fires once when the clock jumps (`30 2 * * *` fires at 03:00 when 02:00 jumps to 03:00). | Skipped times don't fire (`30 * * * *` fires next at 03:30). |
| Repeated time (clock moves back) | Fires only in the first pass. | Fires in both passes. |

## Misfire policy

When the scheduler wakes up so late that the following occurrence of a job has also passed, the job is considered a *misfire*. The `MisfirePolicy` enum controls how missed occurrences are handled. The time passed to `ExecuteAsync` is the scheduled time, or the current time for a misfire.

| Policy | Behavior |
|---|---|
| `CatchUp` (default) | Replays every missed slot back-to-back (a per-minute job delayed by an hour fires ~60 times). |
| `Skip` | Fires once at wake-up, then resumes from the current time; missed slots are discarded. |

`maxCatchUp` limits how many missed slots `CatchUp` replays (`0`, the default, means no limit). When the limit is reached, the remaining missed slots are discarded and the job resumes from the current time.

### Direct scheduler usage

```csharp
// CatchUp is the default; missed occurrences are replayed in order.
scheduler.AddJob("*/1 * * * *", new SampleJob(), "catch-up-job");

// Replay at most 3 missed occurrences.
scheduler.AddJob("*/1 * * * *", new SampleJob(), "limited-job", maxCatchUp: 3);

// Skip past slots and resume from the current time.
scheduler.AddJob("*/1 * * * *", new SampleJob(), "skip-job", MisfirePolicy.Skip);
```

### Dependency injection

```csharp
builder.Services.AddJobSchedulerService(static options =>
{
    // CatchUp (default)
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "catch-up-job");

    // CatchUp with a limit
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "limited-job", maxCatchUp: 3);

    // Skip
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "skip-job", misfirePolicy: MisfirePolicy.Skip);
});
```

## Concurrency policy

The `ConcurrencyPolicy` enum controls what happens when a job is due while its previous execution is still running.

| Policy | Behavior |
|---|---|
| `Allow` (default) | Starts a new execution alongside the running one. |
| `Skip` | Skips the occurrence. This also applies to occurrences replayed by `CatchUp`. |

```csharp
scheduler.AddJob("*/1 * * * *", new SampleJob(), "no-overlap", concurrencyPolicy: ConcurrencyPolicy.Skip);
```

```csharp
builder.Services.AddJobSchedulerService(static options =>
{
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "no-overlap", concurrencyPolicy: ConcurrencyPolicy.Skip);
});
```

## Timeout

`timeout` cancels the `CancellationToken` passed to `ExecuteAsync` when an execution takes longer than the specified time. If the job then ends with an `OperationCanceledException`, it is reported as a `TimeoutException`. A job that ignores the token keeps running.

```csharp
scheduler.AddJob("*/5 * * * *", new SampleJob(), "limited", timeout: TimeSpan.FromMinutes(1));
```

```csharp
builder.Services.AddJobSchedulerService(static options =>
{
    options.UseScopedJob<SampleJob>("*/5 * * * *", name: "limited", timeout: TimeSpan.FromMinutes(1));
});
```

## Dynamic job management

Jobs can be added and removed while the scheduler is running:

```csharp
using Mofucat.JobScheduler;

await using var scheduler = new JobScheduler();

await scheduler.StartAsync();

var handle = scheduler.AddJob("*/10 * * * * *", new SampleJob(), "dynamic");

var found = scheduler.FindJob("dynamic");
if (found is not null)
{
    found.Remove();
}

var removedCount = scheduler.RemoveAllJobs();

await scheduler.StopAsync();
```

`IJobHandle` can also pause, resume, and trigger a job, and reports its execution state:

```csharp
var handle = scheduler.AddJob("0 3 * * *", new SampleJob(), "daily");

handle.Pause();
handle.Resume();
handle.Trigger();

if (handle.LastExecution is { } last)
{
    Console.WriteLine($"{last.Time} {last.Duration} {last.Exception?.Message}");
}
```

| Member | Description |
|---|---|
| `Pause()` | Stops scheduling the job (`NextExecutionTime` becomes `null`). A running execution continues. |
| `Resume()` | Resumes scheduling from the current time. Occurrences missed while paused are not replayed. |
| `Trigger()` | Requests a run now, also while paused. The time passed to `ExecuteAsync` is the current time, and `ConcurrencyPolicy` applies when the run starts, so a skipped run still returns `true`. |
| `State` | `Scheduled`, `Paused`, or `Removed`. |
| `IsRunning` | Whether an execution is in progress. This is independent of `State`: pausing or removing a job does not stop a running execution. |
| `LastExecution` | Time passed to `ExecuteAsync`, duration, and exception (`null` on success) of the last completed execution. |

`Pause()` and `Resume()` return `false` when they have no effect, and `Trigger()` returns `false` when the scheduler is not running; all three return `false` for a removed job.

## Events

`JobError` is raised when an execution fails. `JobCompleted` is raised after every execution with the time passed to `ExecuteAsync`, the duration, and the exception reported to `JobError` (`null` otherwise). Cancellation by `StopAsync` is not reported as an error. Handlers can run on the scheduler loop, so keep them short and do not wait for `StopAsync` in them.

```csharp
scheduler.JobError += static (_, e) => Console.WriteLine($"{e.JobName} failed: {e.Exception.Message}");
scheduler.JobCompleted += static (_, e) => Console.WriteLine($"{e.JobName} {e.Time:HH:mm:ss} {e.Duration.TotalMilliseconds}ms");
```

## Diagnostics

The scheduler publishes an `ActivitySource` and a `Meter` named `Mofucat.JobScheduler`:

| Name | Kind | Tags |
|---|---|---|
| `ExecuteJob` | Root activity for each execution, with error status on failure | `job.name` |
| `scheduler.job.duration` | Histogram of execution duration in seconds | `job.name`, `error.type` on failure |
| `scheduler.job.skipped` | Counter of executions skipped by `ConcurrencyPolicy.Skip` | `job.name` |

`job.name` is set only for jobs registered with a name.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(static tracing => tracing.AddSource("Mofucat.JobScheduler"))
    .WithMetrics(static metrics => metrics.AddMeter("Mofucat.JobScheduler"));
```

## Cron expression

An expression has 5 fields (`minute hour day-of-month month day-of-week`) or 6 fields with a leading `second` field. Each field accepts `*`, values, ranges (`1-5`), steps (`*/15`, `1-30/5`) and lists (`1,15,30`). The month and day-of-week fields also accept names (`JAN`–`DEC`, `SUN`–`SAT`, case-insensitive), including in ranges and lists (`MON-FRI`).

The following macros are supported:

| Macro | Expression |
|---|---|
| `@yearly`, `@annually` | `0 0 1 1 *` |
| `@monthly` | `0 0 1 * *` |
| `@weekly` | `0 0 * * 0` |
| `@daily`, `@midnight` | `0 0 * * *` |
| `@hourly` | `0 * * * *` |

`CronExpression` can be used directly. `GetOccurrences` returns the occurrences from `from` (inclusive) to `to` (exclusive):

```csharp
if (CronExpression.TryParse("0 9 * * MON-FRI", out var expression))
{
    var now = DateTimeOffset.Now;
    var next = expression.GetNextOccurrence(now, TimeZoneInfo.Local);

    foreach (var time in expression.GetOccurrences(now, now.AddDays(7), TimeZoneInfo.Local))
    {
        Console.WriteLine(time);
    }
}
```
