namespace Mofucat.JobScheduler.DependencyInjection;

public sealed class JobRegistration
{
    /// <summary>
    /// ジョブ登録情報の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="cronExpression">実行スケジュールを表す cron 式です。</param>
    /// <param name="name">任意のジョブ名です。</param>
    /// <param name="factory">ジョブ生成ファクトリです。</param>
    public JobRegistration(string cronExpression, string? name, Func<IServiceProvider, ISchedulerJob> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentNullException.ThrowIfNull(factory);

        // ここでは定義情報だけを保持し、実際のジョブ生成は実行開始時に行う。
        CronExpression = cronExpression;
        Name = name;
        Factory = factory;
    }

    /// <summary>
    /// 実行スケジュールを表す cron 式を取得します。
    /// </summary>
    public string CronExpression { get; }

    /// <summary>
    /// ジョブ名を取得します。
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// ジョブ生成に使用するファクトリを取得します。
    /// </summary>
    public Func<IServiceProvider, ISchedulerJob> Factory { get; }
}

public sealed class SchedulerRegistrations
{
    /// <summary>
    /// 登録済みジョブの一覧を取得します。
    /// </summary>
    public List<JobRegistration> Jobs { get; } = [];
}
