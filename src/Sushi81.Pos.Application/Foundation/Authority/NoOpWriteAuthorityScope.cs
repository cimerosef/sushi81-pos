namespace Sushi81.Pos.Application.Foundation.Authority;

internal sealed class NoOpWriteAuthorityScope : IAsyncDisposable
{
    public static NoOpWriteAuthorityScope Instance { get; } = new();

    private NoOpWriteAuthorityScope() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
