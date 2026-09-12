using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>Default production behavior: no manual fault is injected.</summary>
public sealed class NoOpNormalHandoffFaultProbe : INormalHandoffFaultProbe
{
    public static NoOpNormalHandoffFaultProbe Instance { get; } = new();

    private NoOpNormalHandoffFaultProbe()
    {
    }

    public Task BeforeTargetReleasingGrantAsync(
        AuthorityProtocolState pendingState,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Explicit owner-only manual-acceptance probe. It is off by default and is never
/// inferred from transport, remote metadata, timestamps or authority state.
/// </summary>
public sealed class EnvironmentNormalHandoffFaultProbe : INormalHandoffFaultProbe
{
    public const string EnvironmentVariableName = "SUSHI81_M07_MANUAL_FAULT_AFTER_RELINQUISH";

    private readonly bool enabled;

    public EnvironmentNormalHandoffFaultProbe(Func<string?>? readEnvironmentVariable = null)
    {
        enabled = string.Equals(
            (readEnvironmentVariable ?? (() => Environment.GetEnvironmentVariable(EnvironmentVariableName)))(),
            "1",
            StringComparison.Ordinal);
    }

    public Task BeforeTargetReleasingGrantAsync(
        AuthorityProtocolState pendingState,
        CancellationToken cancellationToken = default)
    {
        pendingState.Validate();
        if (!enabled)
            return Task.CompletedTask;

        throw new NormalHandoffFaultInjectedException();
    }
}

public sealed class NormalHandoffFaultInjectedException()
    : IOException("M07 manual acceptance fault injected after durable relinquishment and before grant creation.");
