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

Daylight saving time transitions are handled as follows:

| Case | Behavior |
|---|---|
| Skipped time (clock moves forward) | Fires once when the clock jumps (`30 2 * * *` fires at 03:00 when 02:00 jumps to 03:00). |
| Repeated time (clock moves back) | Fires only in the first pass when the second, minute and hour fields are fixed values. Fires in both passes when any of them uses `*`, a range or a step. |

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
