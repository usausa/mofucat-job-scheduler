namespace Mofucat.JobScheduler.Tests.Mock;

public sealed class DisposableJobState(Exception? executeException, Exception? disposeException)
{
    public Exception? ExecuteException { get; } = executeException;

    public Exception? DisposeException { get; } = disposeException;

    public bool IsDisposed { get; set; }

    public void ThrowDisposeException()
    {
        if (DisposeException is not null)
        {
            throw DisposeException;
        }
    }
}
