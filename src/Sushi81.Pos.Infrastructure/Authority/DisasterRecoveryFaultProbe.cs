namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>
/// Technical, non-authority fault-injection seam for deterministic recovery-boundary tests.
/// The production default is a no-op; it does not persist or derive authority state.
/// </summary>
public interface IDisasterRecoveryFaultProbe
{
    void Hit(DisasterRecoveryFaultPoint point);
}

public enum DisasterRecoveryFaultPoint
{
    BeforePreparingPersistence,
    AfterPreparingPersistence,
    BeforeRemoteActivation,
    AfterRemoteActivation,
    BeforePendingPersistence,
    AfterPendingPersistence,
    BeforeCandidateStaging,
    DuringCandidateStagingValidation,
    AfterCandidateStaging,
    BeforeLocalDatabasePreservation,
    AfterLocalDatabasePreservation,
    BeforeLiveDatabaseReplacement,
    AfterLiveDatabaseReplacement,
    BeforeGenerationAdvance,
    AfterGenerationAdvance,
    BeforeMembershipPublication,
    AfterMembershipPublication,
    BeforeAuthoritativePersistence,
    AfterAuthoritativePersistence,
    BeforeSeedPublication,
    AfterSeedPublication
}

public sealed class NoOpDisasterRecoveryFaultProbe : IDisasterRecoveryFaultProbe
{
    public void Hit(DisasterRecoveryFaultPoint point) { }
}
