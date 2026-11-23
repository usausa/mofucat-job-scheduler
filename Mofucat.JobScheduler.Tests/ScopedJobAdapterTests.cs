namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class ScopedJobAdapterTests
{
    [Fact]
    public async Task ExecuteAsyncWhenJobAndScopeDisposalFailThenThrowsJobException()
    {
        var state = new DisposableJobState(new InvalidOperationException("job error"), new InvalidOperationException("dispose error"));
#pragma warning disable CA2007
        await using var provider = CreateProvider(state);
#pragma warning restore CA2007
        var adapter = new ScopedJobAdapter(provider, typeof(DisposableJob));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(DateTimeOffset.UnixEpoch, CancellationToken.None).AsTask());

        Assert.Same(state.ExecuteException, exception);
        Assert.True(state.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsyncWhenOnlyScopeDisposalFailsThenThrowsDisposalException()
    {
        var state = new DisposableJobState(null, new InvalidOperationException("dispose error"));
#pragma warning disable CA2007
        await using var provider = CreateProvider(state);
#pragma warning restore CA2007
        var adapter = new ScopedJobAdapter(provider, typeof(DisposableJob));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(DateTimeOffset.UnixEpoch, CancellationToken.None).AsTask());

        Assert.Same(state.DisposeException, exception);
        Assert.True(state.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsyncWhenJobFailsAndScopeDisposalThrowsSynchronouslyThenThrowsJobException()
    {
        var state = new DisposableJobState(new InvalidOperationException("job error"), new InvalidOperationException("dispose error"));
#pragma warning disable CA2007
        await using var provider = CreateSyncProvider(state);
#pragma warning restore CA2007
        var adapter = new ScopedJobAdapter(provider, typeof(SyncDisposableJob));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(DateTimeOffset.UnixEpoch, CancellationToken.None).AsTask());

        Assert.Same(state.ExecuteException, exception);
        Assert.True(state.IsDisposed);
    }

    [Fact]
    public async Task ExecuteAsyncWhenJobIsCanceledAndScopeDisposalFailsThenThrowsDisposalException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var state = new DisposableJobState(new OperationCanceledException(cancellationTokenSource.Token), new InvalidOperationException("dispose error"));
#pragma warning disable CA2007
        await using var provider = CreateProvider(state);
#pragma warning restore CA2007
        var adapter = new ScopedJobAdapter(provider, typeof(DisposableJob));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ExecuteAsync(DateTimeOffset.UnixEpoch, cancellationTokenSource.Token).AsTask());

        Assert.Same(state.DisposeException, exception);
    }

    private static ServiceProvider CreateProvider(DisposableJobState state)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new DisposableJob(state));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateSyncProvider(DisposableJobState state)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new SyncDisposableJob(state));
        return services.BuildServiceProvider();
    }
}
