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
