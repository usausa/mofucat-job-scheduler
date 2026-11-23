namespace Mofucat.JobScheduler;

internal sealed class ScheduledJob
{
    private readonly Lock sync = new();

    private int runningCount;

    private volatile bool paused;

    public string Name { get; }

    public string? DiagnosticName { get; }

    public CronExpression Cron { get; }

    public ISchedulerJob Job { get; }

    public JobHandle Handle { get; }

    public MisfirePolicy MisfirePolicy { get; }

    public int MaxCatchUp { get; }

    public TimeZoneInfo? TimeZone { get; }

    public ConcurrencyPolicy ConcurrencyPolicy { get; }

    public TimeSpan? Timeout { get; }

    public DateTimeOffset? Next { get; set; }

    public int CatchUpCount { get; set; }

    public int TriggerCount { get; set; }

    public bool IsPaused
    {
        get => paused;
        set => paused = value;
    }

    public bool IsRunning => Volatile.Read(ref runningCount) > 0;

    public JobExecutionResult? LastExecution
    {
        get
        {
            lock (sync)
            {
                return field;
            }
        }
        private set;
    }

    public ScheduledJob(JobScheduler scheduler, string name, bool named, string cronExpression, CronExpression cron, ISchedulerJob job, MisfirePolicy misfirePolicy, int maxCatchUp, TimeZoneInfo? timeZone, ConcurrencyPolicy concurrencyPolicy, TimeSpan? timeout)
    {
        Name = name;
        DiagnosticName = named ? name : null;
        Cron = cron;
        Job = job;
        MisfirePolicy = misfirePolicy;
        MaxCatchUp = maxCatchUp;
        TimeZone = timeZone;
        ConcurrencyPolicy = concurrencyPolicy;
        Timeout = timeout;
        Handle = new JobHandle(scheduler, this, cronExpression);
    }

    public bool TryBeginExecution()
    {
        if (ConcurrencyPolicy == ConcurrencyPolicy.Skip)
        {
            return Interlocked.CompareExchange(ref runningCount, 1, 0) == 0;
        }

        Interlocked.Increment(ref runningCount);
        return true;
    }

    public void CompleteExecution(DateTimeOffset time, TimeSpan duration, Exception? exception)
    {
        lock (sync)
        {
            LastExecution = new JobExecutionResult(time, duration, exception);
        }
    }

    public void EndExecution() => Interlocked.Decrement(ref runningCount);
}
