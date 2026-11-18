namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class LogSink
{
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => messages;

    public void Add(string message) => messages.Enqueue(message);
}
