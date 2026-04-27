namespace Mofucat.JobScheduler.DependencyInjection;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class SchedulerHostedService : IHostedService
{
    private readonly ILogger<SchedulerHostedService> log;

    private readonly IServiceProvider serviceProvider;

    private readonly JobScheduler scheduler;

    private readonly SchedulerRegistry registry;

    private EventHandler<JobErrorEventArgs>? errorHandler;

    public SchedulerHostedService(ILogger<SchedulerHostedService> log, IServiceProvider serviceProvider, JobScheduler scheduler, SchedulerRegistry registry)
    {
        this.log = log;
        this.serviceProvider = serviceProvider;
        this.scheduler = scheduler;
        this.registry = registry;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in registry.Jobs)
        {
            scheduler.AddJob(registration.CronExpression, registration.Factory(serviceProvider), registration.Name);
        }

        errorHandler = (_, arguments) => log.ErrorSchedulerJobFailed(arguments.Exception, arguments.JobName);
        scheduler.JobError += errorHandler;

        await scheduler.StartAsync().ConfigureAwait(false);

        log.InfoSchedulerStarted();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await scheduler.StopAsync().ConfigureAwait(false);

        if (errorHandler is not null)
        {
            scheduler.JobError -= errorHandler;
            errorHandler = null;
        }

        log.InfoSchedulerStopped();
    }
}
