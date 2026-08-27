namespace Sushi81.Pos.Application.Foundation.Authority;

public sealed class WriteAuthorityException(WriteAuthorityState state)
    : InvalidOperationException($"This device cannot perform an authoritative write while its state is '{state}'.")
{
    public WriteAuthorityState State { get; } = state;
}
