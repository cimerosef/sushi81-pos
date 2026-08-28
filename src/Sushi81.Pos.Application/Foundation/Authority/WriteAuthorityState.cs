namespace Sushi81.Pos.Application.Foundation.Authority;

public enum WriteAuthorityState
{
    Uninitialized,
    Authoritative,
    NonAuthoritativeReadOnly,
    Transitioning,
    RecoveryRequired
}
