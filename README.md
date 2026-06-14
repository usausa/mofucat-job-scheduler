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

## Misfire policy

When the scheduler wakes up late, a job whose scheduled time has already passed is considered a *misfire*. The `MisfirePolicy` enum controls how missed occurrences are handled.

| Policy | Behavior |
|---|---|
| `CatchUp` (default) | Replays every missed slot back-to-back (a per-minute job delayed by an hour fires ~60 times). |
| `Skip` | Fires once at wake-up, then resumes from the current time; missed slots are discarded. |

### Direct scheduler usage

```csharp
// CatchUp is the default; missed occurrences are replayed in order.
scheduler.AddJob("*/1 * * * *", new SampleJob(), "catch-up-job");

// Skip past slots and resume from the current time.
scheduler.AddJob("*/1 * * * *", new SampleJob(), "skip-job", MisfirePolicy.Skip);
```

### Dependency injection

```csharp
builder.Services.AddJobSchedulerService(static options =>
{
    // CatchUp (default)
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "catch-up-job");

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
