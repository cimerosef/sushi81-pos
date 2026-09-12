using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed class WriteAuthorityGuard(WriteAuthorityState initialState = WriteAuthorityState.Uninitialized) : IWriteAuthorityGuard, IDisposable
{
    private readonly SemaphoreSlim transitionGate = new(1, 1);
    public WriteAuthorityState State { get; private set; } = initialState;

    /// <summary>
    /// State changes wait for in-flight business mutations. Callers must not change
    /// state while holding a write scope; the transition is intentionally a barrier.
    /// </summary>
    public void SetState(WriteAuthorityState state)
    {
        transitionGate.Wait();
        try { State = state; }
        finally { transitionGate.Release(); }
    }

    public async ValueTask<IAsyncDisposable> EnterWriteScopeAsync(CancellationToken cancellationToken = default)
    {
        await transitionGate.WaitAsync(cancellationToken);
        try
        {
            RequireWriteAuthority();
            return new WriteAuthorityScope(transitionGate);
        }
        catch
        {
            transitionGate.Release();
            throw;
        }
    }

    public void RequireWriteAuthority()
    {
        if (State != WriteAuthorityState.Authoritative)
        {
            throw new WriteAuthorityException(State);
        }
    }

    public void Dispose() => transitionGate.Dispose();

    private sealed class WriteAuthorityScope(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
