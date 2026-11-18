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

When the scheduler wakes up late, a job whose scheduled time has already passed is considered a *misfire*. The `MisfirePolicy` enum controls how missed occurrences are handled.

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
