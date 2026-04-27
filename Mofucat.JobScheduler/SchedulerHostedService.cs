namespace Mofucat.JobScheduler;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CA1848
public sealed class SchedulerHostedService : IHostedService
{
    private readonly JobScheduler scheduler;
    private readonly SchedulerRegistrations registrations;
    private readonly IServiceProvider rootProvider;
    private readonly ILogger<JobScheduler> logger;
    private EventHandler<JobErrorEventArgs>? errorHandler;

    public SchedulerHostedService(JobScheduler scheduler, SchedulerRegistrations registrations, IServiceProvider rootProvider)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(rootProvider);

        this.scheduler = scheduler;
        this.registrations = registrations;
        this.rootProvider = rootProvider;
        logger = rootProvider.GetService<ILogger<JobScheduler>>() ?? NullLogger<JobScheduler>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in registrations.Jobs)
        {
            scheduler.AddJob(registration.CronExpression, registration.Factory(rootProvider), registration.Name);
        }

        errorHandler = static (_, _) => { };
        errorHandler = (_, arguments) => logger.LogError(arguments.Exception, "Scheduler job '{JobName}' failed", arguments.JobName);
        scheduler.JobError += errorHandler;
        scheduler.Start();
        logger.LogInformation("Scheduler started with {JobCount} registered job(s).", registrations.Jobs.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await scheduler.StopAsync().ConfigureAwait(false);
        if (errorHandler is not null)
        {
            scheduler.JobError -= errorHandler;
            errorHandler = null;
        }

        logger.LogInformation("Scheduler stopped.");
    }
}
#pragma warning restore CA1848
