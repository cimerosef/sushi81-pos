using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;

namespace Sushi81.Pos.Application.Foundation;

/// <summary>
/// Explicit test-only dependencies for legacy unit/UI fixtures. They are internal so production
/// callers cannot construct an application mutation service without the M06 seams.
/// </summary>
internal sealed class TestOnlyAuthoritativeGuard : IWriteAuthorityGuard
{
    public static TestOnlyAuthoritativeGuard Instance { get; } = new();

    public WriteAuthorityState State => WriteAuthorityState.Authoritative;

    public void RequireWriteAuthority() { }
}

internal sealed class TestOnlyDurableChangeNotifier : IDurableChangeNotifier
{
    public static TestOnlyDurableChangeNotifier Instance { get; } = new();

    public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
