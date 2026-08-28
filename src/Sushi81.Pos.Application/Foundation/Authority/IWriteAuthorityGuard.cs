namespace Sushi81.Pos.Application.Foundation.Authority;

public interface IWriteAuthorityGuard
{
    WriteAuthorityState State { get; }

    void RequireWriteAuthority();
}
