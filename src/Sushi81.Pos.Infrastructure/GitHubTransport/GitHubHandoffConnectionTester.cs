using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.GitHubTransport;

/// <summary>Read-only operator setup check; it never creates a release or changes authority.</summary>
public sealed class GitHubHandoffConnectionTester(IGitHubHandoffTransport transport)
{
    public async Task<GitHubConnectionTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await transport.EnsureContainerAsync(createIfMissing: false, cancellationToken);
            return new(true, release, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return new(false, null, exception);
        }
    }
}

public sealed record GitHubConnectionTestResult(
    bool Succeeded,
    GitHubReleaseContainer? Release,
    Exception? Error);
