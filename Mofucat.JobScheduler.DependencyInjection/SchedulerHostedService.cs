namespace Mofucat.JobScheduler.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CA1848
public sealed class SchedulerHostedService : IHostedService
{
    // ホスト ライフサイクルに連動して起動・停止するスケジューラ本体。
    private readonly JobScheduler scheduler;
    // 起動時にスケジューラへ流し込むジョブ登録一覧。
    private readonly SchedulerRegistry registry;
    // ジョブ解決やロガー取得に使うルート サービス プロバイダー。
    private readonly IServiceProvider rootProvider;
    // スケジューラ関連のログ出力先。
    private readonly ILogger<JobScheduler> logger;
    // 購読解除のため保持するエラー通知ハンドラー。
    private EventHandler<JobErrorEventArgs>? errorHandler;

    public SchedulerHostedService(JobScheduler scheduler, SchedulerRegistry registry, IServiceProvider rootProvider)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(rootProvider);

        this.scheduler = scheduler;
        this.registry = registry;
        this.rootProvider = rootProvider;
        // ロガー未登録でも安全に動作するよう NullLogger へフォールバックする。
        logger = rootProvider.GetService<ILogger<JobScheduler>>() ?? NullLogger<JobScheduler>.Instance;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // ホスト開始時に、事前登録されたジョブをスケジューラへ流し込む。
        foreach (var registration in registry.Jobs)
        {
            // ジョブ実体は各登録のファクトリに任せ、ここではスケジュールのみ適用する。
            scheduler.AddJob(registration.CronExpression, registration.Factory(rootProvider), registration.Name);
        }

        // ジョブ例外はホスト側ログへ集約する。
        errorHandler = (_, arguments) => logger.LogError(arguments.Exception, "Scheduler job '{JobName}' failed", arguments.JobName);
        scheduler.JobError += errorHandler;
        scheduler.Start();
        // 起動ログには登録済み件数を残し、構成確認を容易にする。
        logger.LogInformation("Scheduler started with {JobCount} registered job(s).", registry.Jobs.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // 停止時は先にスケジューラを止めてからイベント購読を解除する。
        await scheduler.StopAsync().ConfigureAwait(false);
        if (errorHandler is not null)
        {
            // 再起動時の二重購読を避けるため、確実に購読解除しておく。
            scheduler.JobError -= errorHandler;
            errorHandler = null;
        }

        logger.LogInformation("Scheduler stopped.");
    }
}
#pragma warning restore CA1848
