namespace Mofucat.JobScheduler.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

internal sealed class ScopedJobAdapter : ISchedulerJob
{
    // スコープ生成の起点となるルート サービス プロバイダー。
    private readonly IServiceProvider rootProvider;
    // 実行時に解決するジョブ型。
    private readonly Type jobType;

    public ScopedJobAdapter(IServiceProvider rootProvider, Type jobType)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        ArgumentNullException.ThrowIfNull(jobType);

        // 実行ごとに新しいスコープを張るため、ルート プロバイダーを保持しておく。
        this.rootProvider = rootProvider;
        this.jobType = jobType;
    }

    public async ValueTask ExecuteAsync(DateTimeOffset time, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007
        // 実行ごとにスコープを作成し、スコープ付き依存関係を安全に解決する。
        await using var scope = rootProvider.CreateAsyncScope();
#pragma warning restore CA2007
        // 実ジョブの生成失敗はそのまま上位へ伝播させ、スケジューラ側で通知する。
        var job = (ISchedulerJob)scope.ServiceProvider.GetRequiredService(jobType);
        await job.ExecuteAsync(time, cancellationToken).ConfigureAwait(false);
    }
}
