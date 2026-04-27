namespace Mofucat.JobScheduler.DependencyInjection;

using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// サービス コレクションへジョブ スケジューラ関連のサービスを追加します。
    /// </summary>
    /// <param name="services">登録先のサービス コレクションです。</param>
    /// <param name="options">任意のスケジューラ構成処理です。</param>
    /// <returns>サービス コレクション自身を返します。</returns>
    public static IServiceCollection AddJobScheduler(this IServiceCollection services, Action<JobSchedulerOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddJobSchedulerCore();
        var registrations = GetOrCreateRegistrations(services);
        services.AddJobSchedulerHosting();

        if (options is not null)
        {
            // 呼び出し側の登録構成はここでただちに反映する。
            var schedulerOptions = new JobSchedulerOptions(services, registrations);
            options(schedulerOptions);
        }

        return services;
    }

    /// <summary>
    /// サービス コレクションへジョブ スケジューラのコア機能のみを追加します。
    /// </summary>
    /// <param name="services">登録先のサービス コレクションです。</param>
    /// <returns>サービス コレクション自身を返します。</returns>
    public static IServiceCollection AddJobSchedulerCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddCoreServices(services);
        return services;
    }

    /// <summary>
    /// サービス コレクションへホスト統合用のジョブ スケジューラ機能を追加します。
    /// </summary>
    /// <param name="services">登録先のサービス コレクションです。</param>
    /// <returns>サービス コレクション自身を返します。</returns>
    public static IServiceCollection AddJobSchedulerHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        GetOrCreateRegistrations(services);
        AddHostedServiceServices(services);
        return services;
    }

    /// <summary>
    /// 指定したジョブ型のスケジュール登録を追加します。
    /// </summary>
    /// <typeparam name="T">登録するジョブ型です。</typeparam>
    /// <param name="services">登録先のサービス コレクションです。</param>
    /// <param name="expression">実行スケジュールを表す cron 式です。</param>
    /// <param name="name">任意のジョブ名です。</param>
    /// <returns>サービス コレクション自身を返します。</returns>
    public static IServiceCollection AddJobSchedulerJob<T>(this IServiceCollection services, string expression, string? name = null)
        where T : class, ISchedulerJob
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddJobSchedulerCore();

        var schedulerOptions = new JobSchedulerOptions(services, GetOrCreateRegistrations(services));
        schedulerOptions.UseJob<T>(expression, name);
        return services;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        // TimeProvider を差し替え可能な単一サービスとして提供する。
        services.TryAddSingleton(TimeProvider.System);
        // スケジューラ本体もシングルトンでホスト全体に 1 つだけ持つ。
        services.TryAddSingleton<JobScheduler>();
    }

    private static void AddHostedServiceServices(IServiceCollection services)
    {
        // HostedService として登録することで、ホスト開始・停止に追従させる。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SchedulerHostedService>());
    }

    private static SchedulerRegistry GetOrCreateRegistrations(IServiceCollection services)
    {
        // TODO OfTypeとか？
        // 複数回呼び出されても単一の登録情報インスタンスを再利用する。
        if (services
                .FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(SchedulerRegistry))
                ?.ImplementationInstance is SchedulerRegistry registry)
        {
            return registry;
        }

        var created = new SchedulerRegistry();
        // 登録情報はインスタンス保持だけでよいため、そのまま singleton 登録する。
        services.AddSingleton(created);
        return created;
    }
}
