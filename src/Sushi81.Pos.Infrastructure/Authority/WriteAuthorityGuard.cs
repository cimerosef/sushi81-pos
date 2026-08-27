using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed class WriteAuthorityGuard(WriteAuthorityState initialState = WriteAuthorityState.Uninitialized) : IWriteAuthorityGuard
{
    public WriteAuthorityState State { get; private set; } = initialState;

    public void SetState(WriteAuthorityState state) => State = state;

    public void RequireWriteAuthority()
    {
        if (State != WriteAuthorityState.Authoritative)
        {
            throw new WriteAuthorityException(State);
        }
    }
}
