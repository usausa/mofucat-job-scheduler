namespace Mofucat.JobScheduler.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// DI 統合時のスケジュール済みジョブ登録オプションを提供します。
/// </summary>
public sealed class JobSchedulerOptions
{
    // ジョブ型の DI 登録先。
    private readonly IServiceCollection services;
    // AddJobScheduler で収集したジョブ登録先。
    private readonly SchedulerRegistrations registrations;

    /// <summary>
    /// <see cref="JobSchedulerOptions"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="services">登録先のサービス コレクションです。</param>
    /// <param name="registrations">ジョブ登録情報の格納先です。</param>
    public JobSchedulerOptions(IServiceCollection services, SchedulerRegistrations registrations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrations);

        // ジョブ型を必要に応じて DI へ追加登録するため保持する。
        this.services = services;
        this.registrations = registrations;
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
        registrations.Jobs.Add(new JobRegistration(expression, name, static serviceProvider => new ScopedJobAdapter(serviceProvider, typeof(T))));
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
        registrations.Jobs.Add(new JobRegistration(expression, name, serviceProvider => new ScopedJobAdapter(serviceProvider, jobType)));
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
        registrations.Jobs.Add(new JobRegistration(expression, name, _ => job));
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
        registrations.Jobs.Add(new JobRegistration(expression, name, factory));
    }

    private static void ValidateCronExpression(string expression)
    {
        // 登録時点で cron 式を検証し、誤設定を早期に検出する。
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _ = CronExpression.Parse(expression);
    }
}
