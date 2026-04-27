namespace Mofucat.JobScheduler.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public sealed class JobSchedulerOptions
{
    private readonly IServiceCollection services;

    private readonly SchedulerRegistry registry;

    // TODO internal ?
    public JobSchedulerOptions(IServiceCollection services, SchedulerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);

        // ジョブ型を必要に応じて DI へ追加登録するため保持する。
        this.services = services;
        this.registry = registry;
    }

    /// <summary>
    /// スコープ生成付きでジョブ型 <typeparamref name="T"/> を登録します。
    /// </summary>
    /// <typeparam name="T">登録するジョブ型です。</typeparam>
    /// <param name="expression">実行スケジュールを表す cron 式です。</param>
    /// <param name="name">任意のジョブ名です。</param>
    public void UseJob<T>(string expression, string? name = null)
        where T : class, ISchedulerJob
    {
        ValidateCronExpression(expression);
        services.TryAddScoped<T>();
        // 実行時にスコープを作成し、そのスコープからジョブを解決する。
        registry.Jobs.Add(new JobRegistration(name, expression, static serviceProvider => new ScopedJobAdapter(serviceProvider, typeof(T))));
    }

    /// <summary>
    /// 実行時に指定した型をジョブとして登録します。
    /// </summary>
    /// <param name="expression">実行スケジュールを表す cron 式です。</param>
    /// <param name="jobType">登録するジョブ型です。</param>
    /// <param name="name">任意のジョブ名です。</param>
    public void UseJob(string expression, Type jobType, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(jobType);

        // 登録時点で型制約を満たさないものは早期に拒否する。
        if (!typeof(ISchedulerJob).IsAssignableFrom(jobType))
        {
            throw new ArgumentException($"Type {jobType} does not implement ISchedulerJob.", nameof(jobType));
        }

        // 実装型自身をスコープ解決できるよう DI へ登録する。
        services.TryAdd(ServiceDescriptor.Scoped(jobType, jobType));
        registry.Jobs.Add(new JobRegistration(name, expression, serviceProvider => new ScopedJobAdapter(serviceProvider, jobType)));
    }

    /// <summary>
    /// 既存のジョブ インスタンスをそのまま登録します。
    /// </summary>
    /// <param name="expression">実行スケジュールを表す cron 式です。</param>
    /// <param name="job">登録するジョブ インスタンスです。</param>
    /// <param name="name">任意のジョブ名です。</param>
    public void UseJob(string expression, ISchedulerJob job, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(job);

        // 既存インスタンスは毎回同じ参照を返すファクトリとして保持する。
        registry.Jobs.Add(new JobRegistration(name, expression, _ => job));
    }

    /// <summary>
    /// ファクトリを通じてジョブを生成する登録を追加します。
    /// </summary>
    /// <param name="expression">実行スケジュールを表す cron 式です。</param>
    /// <param name="factory">ジョブ生成に使用するファクトリです。</param>
    /// <param name="name">任意のジョブ名です。</param>
    public void UseJob(string expression, Func<IServiceProvider, ISchedulerJob> factory, string? name = null)
    {
        ValidateCronExpression(expression);
        ArgumentNullException.ThrowIfNull(factory);

        // ジョブ生成責務を完全に呼び出し側へ委ねる拡張ポイント。
        registry.Jobs.Add(new JobRegistration(name, expression, factory));
    }

    private static void ValidateCronExpression(string expression)
    {
        // 登録時点で cron 式を検証し、誤設定を早期に検出する。
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _ = CronExpression.Parse(expression);
    }
}
