using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.GitHubTransport;

public enum GitHubConnectionFailureKind
{
    None,
    NotConfigured,
    CredentialMissing,
    Unauthorized,
    Forbidden,
    NotFound,
    Transport
}

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
            return new(false, null, exception, Classify(exception));
        }
    }

    private static GitHubConnectionFailureKind Classify(Exception exception) => exception switch
    {
        GitHubTransportException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => GitHubConnectionFailureKind.Unauthorized,
        GitHubTransportException { StatusCode: System.Net.HttpStatusCode.Forbidden } => GitHubConnectionFailureKind.Forbidden,
        GitHubTransportException { StatusCode: System.Net.HttpStatusCode.NotFound } => GitHubConnectionFailureKind.NotFound,
        GitHubTransportException transportException when transportException.Message.Contains("credential", StringComparison.OrdinalIgnoreCase)
            => GitHubConnectionFailureKind.CredentialMissing,
        _ => GitHubConnectionFailureKind.Transport
    };
}

public sealed record GitHubConnectionTestResult(
    bool Succeeded,
    GitHubReleaseContainer? Release,
    Exception? Error,
    GitHubConnectionFailureKind FailureKind = GitHubConnectionFailureKind.None);
