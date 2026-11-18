namespace Mofucat.JobScheduler.Tests.Mock;

using Microsoft.Extensions.Logging;

public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly LogSink sink;

    public CapturingLogger(LogSink sink)
    {
        this.sink = sink;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        sink.Add(formatter(state, exception));
    }
}
