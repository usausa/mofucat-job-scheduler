namespace Mofucat.JobScheduler;

using System.Globalization;

/// <summary>
/// ジョブを登録し、cron 式に従って実行するスケジューラです。
/// </summary>
public sealed class JobScheduler : IDisposable, IAsyncDisposable
{
    // 一度に待機する最大時間。長時間待機を分割して構成変更へ追従しやすくする。
    private static readonly TimeSpan MaxSingleWait = TimeSpan.FromHours(1);

    // ジョブ一覧と実行状態を保護する排他ロック。
    private readonly Lock sync = new();
    // ジョブ名をキーとした登録済みジョブ一覧。
    private readonly Dictionary<string, ScheduledJob> jobs = [];
    // 実行ループへ再評価を促す起床シグナル。
    private TaskCompletionSource<bool> wakeup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // 実行ループ停止用のキャンセル ソース。
    private CancellationTokenSource? cancellationTokenSource;
    // 実行ループ本体のタスク。
    private Task? loopTask;
    // スケジューラが開始済みかどうかを表す。
    private bool isRunning;
    // 破棄済みかどうかを表す。
    private bool disposed;
    // 現在時刻取得やタイマー生成に使用する時刻プロバイダー。
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// <see cref="JobScheduler"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="timeProvider">スケジュール評価に使用する時刻プロバイダーです。</param>
    public JobScheduler(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// ジョブ実行中に例外が発生したときに通知されます。
    /// </summary>
    public event EventHandler<JobErrorEventArgs>? JobError;

    /// <summary>
    /// スケジューラが実行中かどうかを取得します。
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return isRunning;
            }
        }
    }

    /// <summary>
    /// 現在登録されているジョブ名の一覧を取得します。
    /// </summary>
    public IReadOnlyList<string> JobNames
    {
        get
        {
            lock (sync)
            {
                var names = new string[jobs.Count];
                jobs.Keys.CopyTo(names, 0);
                return names;
            }
        }
    }

    /// <summary>
    /// スケジューラを開始します。
    /// </summary>
    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (isRunning)
            {
                // 多重開始は無視し、現在の実行ループを維持する。
                return;
            }

            isRunning = true;
            cancellationTokenSource = new CancellationTokenSource();
            wakeup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var now = timeProvider.GetUtcNow();
            foreach (var job in jobs.Values)
            {
                // 開始時点を基準に全ジョブの次回実行時刻を再計算する。
                job.Next = job.Cron.GetNextOccurrence(now);
            }

            loopTask = Task.Run(() => RunLoopAsync(cancellationTokenSource.Token));
        }
    }

    /// <summary>
    /// スケジューラを停止します。
    /// </summary>
    /// <returns>停止処理の完了を表すタスクです。</returns>
    public async Task StopAsync()
    {
        // ロック内では状態の切り替えだけを行い、待機はロック外で実施する。
        CancellationTokenSource? currentCancellationTokenSource;
        Task? currentLoopTask;

        lock (sync)
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            currentCancellationTokenSource = cancellationTokenSource;
            currentLoopTask = loopTask;
            cancellationTokenSource = null;
            loopTask = null;
            SignalWakeupUnsafe();
        }

        if (currentCancellationTokenSource is not null)
        {
            // まずキャンセルを通知して、待機中のループを速やかに抜けさせる。
            await currentCancellationTokenSource.CancelAsync().ConfigureAwait(false);
        }

        if (currentLoopTask is not null)
        {
            try
            {
                // 実行ループの自然終了を待つ。
                await currentLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 停止由来のキャンセルは正常系として扱う。
            }
        }

        currentCancellationTokenSource?.Dispose();
    }

    /// <summary>
    /// スケジューラを同期的に停止します。
    /// </summary>
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    /// <summary>
    /// ジョブをスケジューラへ登録します。
    /// </summary>
    /// <param name="cronExpression">実行スケジュールを表す cron 式です。</param>
    /// <param name="job">実行するジョブ インスタンスです。</param>
    /// <param name="name">任意のジョブ名です。未指定時は自動採番されます。</param>
    /// <returns>登録済みジョブを操作するためのハンドルです。</returns>
    public IJobHandle AddJob(string cronExpression, ISchedulerJob job, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        ArgumentNullException.ThrowIfNull(job);

        var expression = CronExpression.Parse(cronExpression);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            var actualName = name ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            if (jobs.ContainsKey(actualName))
            {
                throw new ArgumentException($"A job named '{actualName}' already exists.", nameof(name));
            }

            var handle = new JobHandle(this, actualName, cronExpression);
            var scheduledJob = new ScheduledJob(actualName, expression, job, handle);
            if (isRunning)
            {
                // 実行中に追加されたジョブは、その場で次回実行時刻を確定する。
                scheduledJob.Next = expression.GetNextOccurrence(timeProvider.GetUtcNow());
            }

            jobs[actualName] = scheduledJob;
            SignalWakeupUnsafe();
            return handle;
        }
    }

    /// <summary>
    /// 指定したジョブ名の登録を解除します。
    /// </summary>
    /// <param name="name">削除対象のジョブ名です。</param>
    /// <returns>ジョブを削除できた場合は <see langword="true"/> です。</returns>
    public bool RemoveJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (sync)
        {
            if (jobs.Remove(name, out var job))
            {
                // ハンドル側からも削除済み判定できるよう状態を反映する。
                job.Handle.MarkRemoved();
                SignalWakeupUnsafe();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 登録されているすべてのジョブを削除します。
    /// </summary>
    /// <returns>削除したジョブ数を返します。</returns>
    public int RemoveAllJobs()
    {
        lock (sync)
        {
            var removedCount = jobs.Count;
            if (removedCount == 0)
            {
                return 0;
            }

            foreach (var job in jobs.Values)
            {
                // 一括削除でも各ハンドルの状態整合性を保つ。
                job.Handle.MarkRemoved();
            }

            jobs.Clear();
            SignalWakeupUnsafe();
            return removedCount;
        }
    }

    /// <summary>
    /// 指定したジョブ名が登録済みかどうかを判定します。
    /// </summary>
    /// <param name="name">確認するジョブ名です。</param>
    /// <returns>ジョブが存在する場合は <see langword="true"/> です。</returns>
    public bool ContainsJob(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (sync)
        {
            return jobs.ContainsKey(name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // 同期破棄ではまず停止を完了させてから破棄済みへ移行する。
        Stop();
        lock (sync)
        {
            disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // 非同期破棄では停止完了を await してから破棄済みへ移行する。
        await StopAsync().ConfigureAwait(false);
        lock (sync)
        {
            disposed = true;
        }
    }

    private void SignalWakeupUnsafe()
    {
        // 待機中のループへ、ジョブ構成が変化したことを即時通知する。
        var currentWakeup = wakeup;
        wakeup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentWakeup.TrySetResult(true);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        // 最短の次回実行時刻を持つジョブだけを毎回選択して待機する。
        while (!cancellationToken.IsCancellationRequested)
        {
            ScheduledJob? nextJob;
            DateTimeOffset? nextTime;
            Task wakeupTask;

            lock (sync)
            {
                nextJob = null;
                nextTime = null;
                foreach (var job in jobs.Values)
                {
                    // 次回実行時刻が最も早いジョブを選択する。
                    if (job.Next is null)
                    {
                        continue;
                    }

                    if (nextTime is null || job.Next < nextTime)
                    {
                        nextTime = job.Next;
                        nextJob = job;
                    }
                }

                wakeupTask = wakeup.Task;
            }

            if (nextJob is null)
            {
                try
                {
                    // 実行対象が無い間は、ジョブ追加や停止要求が来るまで待機する。
                    await WaitAsync(wakeupTask, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var now = timeProvider.GetUtcNow();
            var delay = nextTime!.Value - now;
            if (delay > MaxSingleWait)
            {
                delay = MaxSingleWait;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    // 長すぎる待機は分割し、構成変更や時間進行に追従しやすくする。
                    await WaitAsync(wakeupTask, delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                now = timeProvider.GetUtcNow();
            }

            if (now < nextTime.Value)
            {
                // 待機中にジョブ構成が変わった可能性があるため、再度選び直す。
                continue;
            }

            ScheduledJob? firingJob = null;
            var fireTime = nextTime.Value;
            lock (sync)
            {
                if (jobs.TryGetValue(nextJob.Name, out var current)
                    && ReferenceEquals(current, nextJob)
                    && current.Next == nextTime)
                {
                    // 実行前に次回時刻を先に更新し、連続実行でも取りこぼしを防ぐ。
                    firingJob = current;
                    current.Next = current.Cron.GetNextOccurrence(timeProvider.GetUtcNow());
                }
            }

            if (firingJob is not null)
            {
                // 実際のジョブ実行はループを止めないよう非同期で開始する。
                _ = FireJobAsync(firingJob, fireTime, cancellationToken);
            }
        }
    }

    private async Task WaitAsync(Task wakeupTask, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay == Timeout.InfiniteTimeSpan)
        {
            // 無期限待機では wakeup またはキャンセルのどちらかを待つ。
            var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellationTaskSource))
            {
                await Task.WhenAny(wakeupTask, cancellationTaskSource.Task).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        // 有期限待機では TimeProvider によるタイマーを使用し、テスト容易性を確保する。
        var delayTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA2007
        await using var timer = timeProvider.CreateTimer(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            delayTaskSource,
            delay,
            Timeout.InfiniteTimeSpan);
#pragma warning restore CA2007
#pragma warning disable CA2007
        await using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            delayTaskSource);
#pragma warning restore CA2007

        await Task.WhenAny(wakeupTask, delayTaskSource.Task).ConfigureAwait(false);

        try
        {
            // タイマー完了かキャンセル完了のどちらかを確定させる。
            await delayTaskSource.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 下で cancellationToken を再評価するため、ここでは握りつぶす。
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task FireJobAsync(ScheduledJob job, DateTimeOffset fireTime, CancellationToken cancellationToken)
    {
#pragma warning disable CA1031
        try
        {
            // ジョブへは計算済みの発火時刻を渡し、実行開始遅延の影響を避ける。
            await job.Job.ExecuteAsync(fireTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 停止時キャンセルは想定内のため通知しない。
        }
        catch (Exception ex)
        {
            // ジョブ例外はスケジューラ全体を止めず、イベント経由で通知する。
            JobError?.Invoke(this, new JobErrorEventArgs(job.Name, ex));
        }
#pragma warning restore CA1031
    }
}
