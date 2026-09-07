namespace Sushi81.Pos.Application.Foundation.Authority;

public interface IWriteAuthorityGuard
{
    WriteAuthorityState State { get; }

    void RequireWriteAuthority();

    /// <summary>
    /// Acquires a scope that keeps the current authoritative state stable until the
    /// application-owned business mutation has completed. Legacy test guards may use
    /// the default point-check implementation; production guards override this method.
    /// </summary>
    ValueTask<IAsyncDisposable> EnterWriteScopeAsync(CancellationToken cancellationToken = default)
    {
        RequireWriteAuthority();
        return ValueTask.FromResult<IAsyncDisposable>(NoOpWriteAuthorityScope.Instance);
    }
}
