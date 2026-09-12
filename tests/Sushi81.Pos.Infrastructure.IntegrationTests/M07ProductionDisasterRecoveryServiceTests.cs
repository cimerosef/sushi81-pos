using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ProductionDisasterRecoveryServiceTests
{
    private static readonly JsonSerializerOptions TestJsonOptions = new(JsonSerializerDefaults.Web);
    [TestMethod]
    public async Task ServiceWonCommitsPendingThenAuthoritativeAfterExactDataFirstInstall()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, state.Phase);
        Assert.AreEqual(2, state.Generation);
        CollectionAssert.Contains(fixture.SavedPhases.ToArray(), AuthorityPhase.DisasterRecoveryPending);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        Assert.IsNotNull(state.LastRecovery);
    }

    [TestMethod]
    public async Task ServiceUnknownCreateOutcomeReobservesSameWinnerAndCompletes()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.UnknownAfterUpload = true;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.IsTrue(fixture.Transport.UnknownOutcomeWasReobserved);
        Assert.AreEqual(1, fixture.Transport.UploadCount);
        Assert.HasCount(1, fixture.Transport.ActivationAssets);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task ServiceLostToExistingWinnerFencesNewGenerationAndNeverWrites()
    {
        using var fixture = await ServiceFixture.CreateAsync(systemGeneration: 2);
        fixture.Transport.OtherWinnerOnUpload = true;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath), "A losing activation must not install business data.");
    }

    [TestMethod]
    public async Task ServiceBlockedNoWinnerLeavesExactPreparingStateReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.AddIncompleteStarter(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        fixture.Transport.AddIncompleteStarter(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(0, fixture.Transport.DeleteCount, "Contradictory occupants forbid broad cleanup.");
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task ServiceDeletesOnlyOneExactIncompleteStarterAndRetries()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.AddIncompleteStarter(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual(1, fixture.Transport.DeleteCount);
        Assert.AreEqual(1, fixture.Transport.UploadCount);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task ServiceNeverDeletesACompleteContradictoryActivationOccupant()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.AddCompleteOccupant(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, fixture.Transport.DeleteCount);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    [DataRow("401")]
    [DataRow("403")]
    [DataRow("404")]
    [DataRow("5xx")]
    [DataRow("timeout")]
    public async Task ServiceTransportUnavailableOrCredentialFailureFailsClosed(string failure)
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.EnsureFailure = failure;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashBeforePreparingPersistenceDoesNotCreateRemoteActivationOrWriter()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailOnSaveNumber = 1;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, fixture.Transport.UploadCount);
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task CrashAfterActivationBeforePendingPersistenceRestartsExactRecovery()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailOnSaveNumber = 2;
        await using (var first = fixture.CreateService())
        {
            var result = await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);
            Assert.IsFalse(result.Succeeded);
        }

        var pending = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, pending.Phase);
        Assert.IsNotNull(pending.Recovery);
        Assert.AreEqual(fixture.Candidate.CandidateId, pending.Recovery!.CandidateId);
        fixture.FailOnSaveNumber = null;

        await using var restart = fixture.CreateService();
        var resumed = await restart.RetryAsync(true);

        Assert.IsTrue(resumed.Succeeded, resumed.Diagnostic);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashAtExactCandidateStagingLeavesPendingAndReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailCandidateValidation = true;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, fixture.Guard.State);
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashAtSystemGenerationAdvanceLeavesInstalledDataPendingAndReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailSystemOperation = "advance";
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashAtMembershipPublicationLeavesNoAuthoritativeState()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailSystemOperation = "join";
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashImmediatelyBeforeAuthoritativePersistenceRestartsWithoutCandidateSubstitution()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailOnSaveNumber = 3;
        await using (var first = fixture.CreateService())
        {
            Assert.IsFalse((await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true)).Succeeded);
        }

        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, state.Phase);
        Assert.AreEqual(fixture.Candidate.CandidateId, state.Recovery!.CandidateId);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        fixture.FailOnSaveNumber = null;

        await using var restart = fixture.CreateService();
        Assert.IsTrue((await restart.RetryAsync(true)).Succeeded);
        var final = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, final.Phase);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, final.LastRecovery!.CandidateSha256);
    }

    [TestMethod]
    public async Task OptionalSeedPublicationFailureDoesNotRevokeDurableAuthority()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailSystemOperation = "publish-seed";
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
    }

    [TestMethod]
    public async Task AfterAuthoritativePersistenceRestartUsesProductionStartupReconstruction()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Store.WriteBootstrapMarkerAsync();
        await fixture.Store.WriteBootstrapAnchorAsync();
        fixture.Faults.ThrowAt = DisasterRecoveryFaultPoint.AfterAuthoritativePersistence;

        await using (var first = fixture.CreateService())
        {
            var result = await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);
            Assert.IsFalse(result.Succeeded);
        }

        var durable = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, durable.Phase);
        Assert.IsNotNull(durable.LastRecovery);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, durable.LastRecovery!.CandidateSha256);
        Assert.AreEqual(fixture.Candidate.BusinessRevision, durable.LastRecovery.BusinessRevision);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));

        fixture.Faults.ThrowAt = null;
        using var restartedGuard = new WriteAuthorityGuard();
        var resolution = await new AuthorityStateCoordinator(
            new JsonAuthorityStateStore(fixture.Paths),
            restartedGuard,
            fixture.Clock,
            NullLogger<AuthorityStateCoordinator>.Instance,
            fixture.SystemMetadata).InitializeAsync(false, true);

        Assert.IsTrue(resolution.IsUsable);
        Assert.AreEqual(WriteAuthorityState.Authoritative, resolution.State);
        Assert.AreEqual(WriteAuthorityState.Authoritative, restartedGuard.State);
        var restarted = (await new JsonAuthorityStateStore(fixture.Paths).LoadAsync())!.Protocol!;
        Assert.AreEqual(durable.Revision, restarted.Revision);
        Assert.AreEqual(durable.Generation, restarted.Generation);
        Assert.AreEqual(durable.DeviceId, restarted.DeviceId);
        Assert.AreEqual(durable.LineageId, restarted.LineageId);
        Assert.AreEqual(durable.LastRecovery, restarted.LastRecovery);
        Assert.AreEqual(fixture.Candidate.CandidateId, restarted.LastRecovery!.CandidateId);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, restarted.LastRecovery.CandidateSha256);
        Assert.AreEqual(fixture.Candidate.BusinessRevision, await ReadBusinessRevisionAsync(fixture.Paths.LiveDatabasePath));
        Assert.HasCount(1, fixture.Transport.ActivationAssets);
        restartedGuard.RequireWriteAuthority();
    }

    [TestMethod]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforeSeedPublication))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterSeedPublication))]
    public async Task SeedFaultRestartRemainsAuthoritativeAndRetryIsExactIdempotent(string pointName)
    {
        using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Store.WriteBootstrapMarkerAsync();
        await fixture.Store.WriteBootstrapAnchorAsync();
        fixture.Faults.ThrowAt = Enum.Parse<DisasterRecoveryFaultPoint>(pointName);

        await using (var first = fixture.CreateService())
        {
            var result = await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);
            Assert.IsTrue(result.Succeeded, result.Diagnostic);
        }

        var durable = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, durable.Phase);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        var seedsAfterFault = fixture.SeedFileCount;
        Assert.AreEqual(pointName == nameof(DisasterRecoveryFaultPoint.AfterSeedPublication) ? 1 : 0, seedsAfterFault);

        fixture.SystemMetadata.Unavailable = true;
        fixture.Faults.ThrowAt = null;
        using var restartedGuard = new WriteAuthorityGuard();
        var resolution = await new AuthorityStateCoordinator(
            new JsonAuthorityStateStore(fixture.Paths),
            restartedGuard,
            fixture.Clock,
            NullLogger<AuthorityStateCoordinator>.Instance,
            fixture.SystemMetadata).InitializeAsync(false, true);
        Assert.IsTrue(resolution.IsUsable);
        Assert.AreEqual(WriteAuthorityState.Authoritative, resolution.State);
        Assert.AreEqual(WriteAuthorityState.Authoritative, restartedGuard.State);
        Assert.AreEqual(seedsAfterFault, fixture.SeedFileCount);

        fixture.SystemMetadata.Unavailable = false;
        await using (var retry = new DisasterRecoveryService(
            new JsonAuthorityStateStore(fixture.Paths),
            restartedGuard,
            fixture.SystemMetadata,
            fixture.Candidates,
            new RecoveryActivationService(fixture.Transport),
            fixture.Transport,
            fixture.SnapshotService,
            fixture.Paths,
            fixture.Clock,
            fixture.Faults))
        {
            Assert.IsTrue(await retry.TryPublishCurrentGenerationSeedAsync(durable));
            Assert.IsTrue(await retry.TryPublishCurrentGenerationSeedAsync(durable));
        }

        Assert.AreEqual(1, fixture.SeedFileCount);
        Assert.AreEqual(1, fixture.SystemMetadata.PublishCount);
        var seed = await fixture.SystemMetadata.Inner.FindValidatedReadOnlySeedAsync(fixture.LineageId, durable.Generation);
        Assert.IsNotNull(seed);
        Assert.AreEqual(durable.LineageId, seed!.Metadata.LineageId);
        Assert.AreEqual(durable.Generation, seed.Metadata.Generation);
        Assert.AreEqual(durable.BusinessRevision, seed.Metadata.BusinessRevision);
        Assert.AreEqual(durable.HandoffVersion, seed.Metadata.HandoffVersion);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, seed.Metadata.PayloadSha256);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(seed.PayloadPath));
        Assert.AreEqual(WriteAuthorityState.Authoritative, restartedGuard.State);
        restartedGuard.RequireWriteAuthority();
    }

    [TestMethod]
    public async Task StaleGenerationWithReadableNewerLineageRemainsReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task StaleReinitializationWithoutSeedIsBlockedReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await using var service = fixture.CreateService();

        var result = await service.ReinitializeStaleDeviceAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task StaleReinitializationRejectsCorruptCurrentGenerationSeed()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        var seed = await fixture.PublishSeedAsync(22);
        File.WriteAllBytes(seed.PayloadPath, [1, 2, 3]);
        await using var service = fixture.CreateService();

        var result = await service.ReinitializeStaleDeviceAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task SuccessfulStaleReinitializationPreservesOldDataInstallsSeedAndJoinsCurrentGenerationReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await fixture.CreateLiveDatabaseAsync(3, "old-only-marker");
        var oldHash = await HashAsync(fixture.Paths.LiveDatabasePath);
        var seed = await fixture.PublishSeedAsync(22, "current-only-marker");
        await using var service = fixture.CreateService();

        var result = await service.ReinitializeStaleDeviceAsync();

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, state.Phase);
        Assert.AreEqual(2, state.Generation);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(seed.Metadata.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        Assert.AreEqual(1, fixture.SnapshotService.CreateCalls, "Old local data is preserved before replacement.");
        Assert.AreEqual(oldHash, fixture.SnapshotService.CapturedDatabaseHash, "The old database was captured before replacement.");
        CollectionAssert.Contains(await ReadMarkersAsync(fixture.Paths.LiveDatabasePath), "current-only-marker");
        CollectionAssert.DoesNotContain(await ReadMarkersAsync(fixture.Paths.LiveDatabasePath), "old-only-marker");
        var devices = await fixture.SystemMetadata.ListCurrentGenerationDevicesAsync(fixture.LineageId, 2);
        Assert.IsTrue(devices.Any(device => device.DeviceId == fixture.DeviceId));
    }

    [TestMethod]
    public async Task StaleGenerationRejectsHistoricalAcquisitionAndSourceTransferRetry()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await fixture.Store.WriteBootstrapMarkerAsync();
        await fixture.Store.WriteBootstrapAnchorAsync();
        await using var acquisition = new TargetAcquisitionService(
            fixture.Store,
            fixture.Guard,
            fixture.SystemMetadata.Inner,
            fixture.Transport,
            new SqliteTransferSnapshotInstaller(fixture.Paths),
            fixture.Clock);

        var acquisitionResult = await acquisition.AcquireAsync();
        Assert.IsFalse(acquisitionResult.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);

        await using var handoff = new NormalHandoffService(
            fixture.Store,
            fixture.Guard,
            fixture.SystemMetadata.Inner,
            new UnsupportedSnapshotFactory(),
            fixture.Transport,
            fixture.Clock);
        var retry = await handoff.ResumePendingTransferAsync();
        Assert.IsFalse(retry.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task StaleGenerationHistoricalGrantAndSourceRetryRemainReadOnlyAndUntouched()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await fixture.Store.WriteBootstrapMarkerAsync();
        await fixture.Store.WriteBootstrapAnchorAsync();
        fixture.InstallHistoricalReplayEvidence();
        var remoteBefore = fixture.Transport.AssetFingerprints;

        await using var acquisition = new TargetAcquisitionService(
            fixture.Store,
            fixture.Guard,
            fixture.SystemMetadata.Inner,
            fixture.Transport,
            new SqliteTransferSnapshotInstaller(fixture.Paths),
            fixture.Clock);
        var acquisitionResult = await acquisition.AcquireAsync();

        Assert.IsFalse(acquisitionResult.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);

        await using var handoff = new NormalHandoffService(
            fixture.Store,
            fixture.Guard,
            fixture.SystemMetadata.Inner,
            new UnsupportedSnapshotFactory(),
            fixture.Transport,
            fixture.Clock);
        var retry = await handoff.ResumePendingTransferAsync();

        Assert.IsFalse(retry.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        CollectionAssert.AreEqual(remoteBefore.ToArray(), fixture.Transport.AssetFingerprints.ToArray());
        Assert.AreEqual(0, fixture.Transport.DeleteCount);
        Assert.AreEqual(0, fixture.Transport.UploadCount);
        Assert.AreEqual(0, fixture.Transport.ListCalls);
        Assert.AreEqual(0, fixture.Transport.GetCalls);
    }

    [TestMethod]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforeRemoteActivation))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterRemoteActivation))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforePendingPersistence))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterPendingPersistence))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforeCandidateStaging))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.DuringCandidateStagingValidation))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterCandidateStaging))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforeGenerationAdvance))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterGenerationAdvance))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforeMembershipPublication))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterMembershipPublication))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.BeforeAuthoritativePersistence))]
    [DataRow(nameof(DisasterRecoveryFaultPoint.AfterAuthoritativePersistence))]
    public async Task ProductionRecoveryCrashBoundariesRestartExactDurableIdentity(string pointName)
    {
        using var fixture = await ServiceFixture.CreateAsync();
        var point = Enum.Parse<DisasterRecoveryFaultPoint>(pointName);
        if (point is DisasterRecoveryFaultPoint.BeforeLocalDatabasePreservation
            or DisasterRecoveryFaultPoint.AfterLocalDatabasePreservation
            or DisasterRecoveryFaultPoint.BeforeLiveDatabaseReplacement
            or DisasterRecoveryFaultPoint.AfterLiveDatabaseReplacement)
            await fixture.CreateLiveDatabaseAsync(3);

        fixture.Faults.ThrowAt = point;
        await using (var first = fixture.CreateService())
        {
            var result = await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);
            Assert.IsFalse(result.Succeeded, pointName);
        }

        var afterFailure = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreNotEqual(WriteAuthorityState.Authoritative, fixture.Guard.State, pointName);
        if (point == DisasterRecoveryFaultPoint.BeforeRemoteActivation)
            Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, afterFailure.Phase, pointName);
        else if (point == DisasterRecoveryFaultPoint.AfterAuthoritativePersistence)
        {
            Assert.AreEqual(AuthorityPhase.Authoritative, afterFailure.Phase, pointName);
            using var restartedGuard = new WriteAuthorityGuard(afterFailure.WriteState);
            restartedGuard.RequireWriteAuthority();
            return;
        }
        else
        {
            Assert.IsTrue(afterFailure.Phase is AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending, pointName);
            Assert.IsNotNull(afterFailure.Recovery, pointName);
            Assert.AreEqual(fixture.Candidate.CandidateId, afterFailure.Recovery!.CandidateId, pointName);
            Assert.AreEqual(fixture.Candidate.PayloadSha256, afterFailure.Recovery.CandidateSha256, pointName);
            Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath)
                && (point is DisasterRecoveryFaultPoint.BeforeCandidateStaging or DisasterRecoveryFaultPoint.DuringCandidateStagingValidation),
                "A failed staging boundary must not install a live database.");
            Assert.Throws<WriteAuthorityException>(fixture.Guard.RequireWriteAuthority, pointName);
        }

        var newer = fixture.CreateCandidate(99, "checkpoint-newer-after-crash");
        fixture.Candidates.Add(newer);
        fixture.Faults.ThrowAt = null;
        await using var restart = fixture.CreateService();
        var resumed = await restart.RetryAsync(true);

        Assert.IsTrue(resumed.Succeeded, resumed.Diagnostic);
        var final = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, final.Phase);
        Assert.AreEqual(fixture.Candidate.CandidateId, final.LastRecovery!.CandidateId, "Restart must not substitute the newer candidate.");
        Assert.AreEqual(fixture.Candidate.PayloadSha256, final.LastRecovery.CandidateSha256);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        Assert.AreEqual(fixture.Candidate.BusinessRevision, final.LastRecovery.BusinessRevision);
    }

    [TestMethod]
    public async Task ProductionRecoveryPreservationAndReplacementBoundariesKeepOldDataRecoverable()
    {
        foreach (var point in new[]
        {
            DisasterRecoveryFaultPoint.BeforeLocalDatabasePreservation,
            DisasterRecoveryFaultPoint.AfterLocalDatabasePreservation,
            DisasterRecoveryFaultPoint.BeforeLiveDatabaseReplacement,
            DisasterRecoveryFaultPoint.AfterLiveDatabaseReplacement
        })
        {
            using var fixture = await ServiceFixture.CreateAsync();
            await fixture.CreateLiveDatabaseAsync(3);
            var oldHash = await HashAsync(fixture.Paths.LiveDatabasePath);
            fixture.Faults.ThrowAt = point;
            await using (var first = fixture.CreateService())
                Assert.IsFalse((await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true)).Succeeded, point.ToString());

            var afterFailure = (await fixture.Store.LoadAsync())!.Protocol!;
            Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, afterFailure.Phase, point.ToString());
            Assert.Throws<WriteAuthorityException>(fixture.Guard.RequireWriteAuthority, point.ToString());
            if (point is DisasterRecoveryFaultPoint.BeforeLocalDatabasePreservation
                or DisasterRecoveryFaultPoint.AfterLocalDatabasePreservation
                or DisasterRecoveryFaultPoint.BeforeLiveDatabaseReplacement)
                Assert.AreEqual(oldHash, await HashAsync(fixture.Paths.LiveDatabasePath), point.ToString());
            else
                Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath), point.ToString());

            fixture.Faults.ThrowAt = null;
            await using var restart = fixture.CreateService();
            Assert.IsTrue((await restart.RetryAsync(true)).Succeeded, point.ToString());
            Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        }
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task<long> ReadBusinessRevisionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        return long.Parse((string)(await command.ExecuteScalarAsync())!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string[]> ReadMarkersAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT marker FROM synthetic_markers ORDER BY marker";
        var markers = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) markers.Add(reader.GetString(0));
        return markers.ToArray();
    }

    private sealed class ServiceFixture : IDisposable
    {
        private ServiceFixture(AuthorityPhase phase, long systemGeneration)
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Service", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Paths = new ServicePaths(Root);
            SnapshotService = new SnapshotRecorder(Paths);
            LineageId = Guid.NewGuid();
            DeviceId = Guid.NewGuid();
            Clock = new FixedClock();
            Guard = new WriteAuthorityGuard(phase == AuthorityPhase.StaleGeneration ? WriteAuthorityState.NonAuthoritativeReadOnly : WriteAuthorityState.NonAuthoritativeReadOnly);
            SystemMetadata = new FaultingSystemMetadataStore(new JsonSystemMetadataStore(Path.Combine(Root, "OneDrive"), new FixedTimeProvider(Clock.UtcNow)), this);
            ProbeStore = new JsonAuthorityStateStore(Paths, Probe);
            Store = new RecordingAuthorityStore(ProbeStore, SavedPhases);
            Transport = new ActivationTransport();
            Candidates = new ServiceCandidateDiscovery(this);
            var state = new AuthorityProtocolState(1, DeviceId, "Recovery", LineageId, 1, 0, 3, phase);
            ProbeStore.SaveAsync(new AuthorityStateDocument(2, state.WriteState, Clock.UtcNow) { Protocol = state }).GetAwaiter().GetResult();
            SaveNumber = 0;
            SystemMetadata.Inner.EnsureCurrentLineageAsync(LineageId, systemGeneration).GetAwaiter().GetResult();
            Candidate = CreateCandidate();
            Candidates.Set(Candidate);
        }

        public string Root { get; }
        public Guid LineageId { get; }
        public Guid DeviceId { get; }
        public ServicePaths Paths { get; }
        public FixedClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public FaultingSystemMetadataStore SystemMetadata { get; }
        public JsonAuthorityStateStore ProbeStore { get; }
        public RecordingAuthorityStore Store { get; }
        public ActivationTransport Transport { get; }
        public ServiceCandidateDiscovery Candidates { get; }
        public RecoveryCandidate Candidate { get; }
        public List<AuthorityPhase> SavedPhases { get; } = [];
        public int? FailOnSaveNumber { get; set; }
        public int SaveNumber { get; private set; }
        public string? FailSystemOperation { get; set; }
        public bool FailCandidateValidation { get; set; }
        public RecoveryFaultProbe Faults { get; } = new();
        public SnapshotRecorder SnapshotService { get; }
        public int SeedFileCount => Directory.Exists(Path.Combine(SystemMetadata.Inner.SystemDirectoryPath, "Seeds", "2"))
            ? Directory.GetFiles(Path.Combine(SystemMetadata.Inner.SystemDirectoryPath, "Seeds", "2"), "*.seed.json").Length
            : 0;

        public static Task<ServiceFixture> CreateAsync(AuthorityPhase phase = AuthorityPhase.NonAuthoritativeReadOnly, long systemGeneration = 1) => Task.FromResult(new ServiceFixture(phase, systemGeneration));

        public DisasterRecoveryService CreateService() => new(
            Store,
            Guard,
            SystemMetadata,
            Candidates,
            new RecoveryActivationService(Transport),
            Transport,
            SnapshotService,
            Paths,
            Clock,
            Faults);

        public async Task<SeedVector> PublishSeedAsync(long revision, string? marker = null)
        {
            var bytes = CreateDatabase(revision, marker);
            var seedId = Guid.NewGuid();
            var metadata = new ReadOnlySeedMetadata(
                1, "M07", SystemMetadataContract.ReadOnlySeedArtifactKind, seedId, LineageId, 2,
                Guid.NewGuid(), revision, SystemMetadataContract.SeedPayloadFileName(seedId), bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)), Clock.UtcNow, 4);
            await SystemMetadata.Inner.PublishReadOnlySeedAsync(metadata, bytes);
            return new SeedVector(metadata, Path.Combine(SystemMetadata.Inner.SystemDirectoryPath, "Seeds", "2", metadata.PayloadFileName));
        }

        public async Task CreateLiveDatabaseAsync(long revision, string? marker = null)
        {
            Paths.EnsureInitialized();
            await File.WriteAllBytesAsync(Paths.LiveDatabasePath, CreateDatabase(revision, marker));
        }

        public void InstallHistoricalReplayEvidence()
        {
            var snapshotBytes = CreateDatabase(3, "historical-old-generation");
            var transferId = Guid.NewGuid();
            var sourceDeviceId = Guid.NewGuid();
            var snapshotName = "20260907110000.snapshot.db";
            var snapshotHash = Convert.ToHexString(SHA256.HashData(snapshotBytes));
            var snapshotReceipt = Transport.AddHistoricalAsset(snapshotName, snapshotBytes);
            var grant = new NormalHandoffGrant(
                "M07",
                transferId,
                LineageId,
                1,
                1,
                sourceDeviceId,
                DeviceId,
                3,
                snapshotReceipt,
                Clock.UtcNow.AddMinutes(-2),
                Clock.UtcNow.AddMinutes(-2));
            var grantBytes = JsonSerializer.SerializeToUtf8Bytes(grant, TestJsonOptions);
            var grantReceipt = Transport.AddHistoricalAsset("20260907110000.grant.json", grantBytes);
            var prior = new RecoveryPriorTransferEvidence(
                AuthorityPhase.ReleasedNonAuthoritative,
                transferId,
                LineageId,
                1,
                1,
                sourceDeviceId,
                DeviceId,
                3,
                snapshotReceipt,
                grantReceipt);
            var recovery = new RecoveryActivationEvidence(
                Guid.NewGuid(),
                DeviceId,
                LineageId,
                1,
                2,
                "historical-recovery-candidate",
                new string('A', 64),
                3,
                CandidateType: "",
                CandidateReference: "",
                CandidateHandoffVersion: 1,
                CandidateCreatedAtUtc: Clock.UtcNow.AddMinutes(-1),
                PriorTransfer: prior);
            var current = ProbeStore.LoadAsync().GetAwaiter().GetResult()!.Protocol!;
            var stale = current with
            {
                Revision = current.Revision + 1,
                HandoffVersion = 1,
                LastRecovery = recovery,
                Phase = AuthorityPhase.StaleGeneration,
                Transfer = null,
                Recovery = null
            };
            stale.Validate();
            ProbeStore.SaveAsync(new AuthorityStateDocument(2, stale.WriteState, Clock.UtcNow) { Protocol = stale }).GetAwaiter().GetResult();
        }

        public RecoveryCandidate CreateCandidate(long revision, string candidateId)
        {
            var path = Path.Combine(Root, $"{candidateId}.db");
            var bytes = CreateDatabase(revision);
            File.WriteAllBytes(path, bytes);
            return new RecoveryCandidate(RecoveryCandidateType.OneDriveCheckpoint, candidateId, LineageId, 1, Guid.NewGuid(), revision, 4, Clock.UtcNow, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)), path);
        }

        private RecoveryCandidate CreateCandidate() => CreateCandidate(8, "checkpoint-exact-a");

        private void Probe(string eventName)
        {
            if (eventName == "before-replace")
            {
                SaveNumber++;
                if (FailOnSaveNumber == SaveNumber) throw new IOException($"synthetic crash at authority save {SaveNumber}");
            }
        }

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static byte[] CreateDatabase(long revision, string? marker = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"m07-service-db-{Guid.NewGuid():N}.db");
            try
            {
                using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','{revision}'); CREATE TABLE synthetic_markers(marker TEXT NOT NULL PRIMARY KEY);";
                    command.ExecuteNonQuery();
                    if (marker is not null)
                    {
                        using var markerCommand = connection.CreateCommand();
                        markerCommand.CommandText = "INSERT INTO synthetic_markers(marker) VALUES ($marker);";
                        markerCommand.Parameters.AddWithValue("$marker", marker);
                        markerCommand.ExecuteNonQuery();
                    }
                }
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }

    private sealed record SeedVector(ReadOnlySeedMetadata Metadata, string PayloadPath);

    private sealed class ServiceCandidateDiscovery(ServiceFixture fixture) : IRecoveryCandidateDiscovery
    {
        private readonly List<RecoveryCandidate> candidates = [];
        public void Set(RecoveryCandidate value)
        {
            candidates.Clear();
            candidates.Add(value);
        }

        public void Add(RecoveryCandidate value) => candidates.Add(value);

        public Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default)
        {
            var values = candidates.Select(value => fixture.FailCandidateValidation ? value with { PayloadSha256 = new string('F', 64) } : value).ToArray();
            return Task.FromResult(new RecoveryCandidateDiscoveryResult(values, "synthetic production-service candidate"));
        }

        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default)
        {
            var value = candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId);
            if (value is null) return Task.FromResult<RecoveryCandidate?>(null);
            return Task.FromResult<RecoveryCandidate?>(fixture.FailCandidateValidation ? value with { PayloadSha256 = new string('F', 64) } : value);
        }
    }

    private sealed class RecoveryFaultProbe : IDisasterRecoveryFaultProbe
    {
        public DisasterRecoveryFaultPoint? ThrowAt { get; set; }

        public void Hit(DisasterRecoveryFaultPoint point)
        {
            if (ThrowAt == point)
                throw new IOException($"synthetic recovery fault at {point}");
        }
    }

    private sealed class RecordingAuthorityStore(IAuthorityStateStore inner, List<AuthorityPhase> savedPhases) : IAuthorityStateStore
    {
        public Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);
        public async Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default)
        {
            if (document.Protocol is { } protocol) savedPhases.Add(protocol.Phase);
            await inner.SaveAsync(document, cancellationToken);
        }
        public Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default) => inner.HasBootstrapMarkerAsync(cancellationToken);
        public Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default) => inner.WriteBootstrapMarkerAsync(cancellationToken);
        public Task<bool> HasLegacyBootstrapEvidenceAsync(CancellationToken cancellationToken = default) => inner.HasLegacyBootstrapEvidenceAsync(cancellationToken);
        public Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default) => inner.HasBootstrapAnchorAsync(cancellationToken);
        public Task WriteBootstrapAnchorAsync(CancellationToken cancellationToken = default) => inner.WriteBootstrapAnchorAsync(cancellationToken);
        public Task<bool> HasEstablishedAuthorityArtifactsAsync(CancellationToken cancellationToken = default) => inner.HasEstablishedAuthorityArtifactsAsync(cancellationToken);
    }

    private sealed class FaultingSystemMetadataStore(JsonSystemMetadataStore inner, ServiceFixture fixture) : ISystemMetadataStore
    {
        public JsonSystemMetadataStore Inner { get; } = inner;
        public bool Unavailable { get; set; }
        public int PublishCount { get; private set; }
        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Unavailable ? throw new SystemMetadataUnavailableException("synthetic System outage") : Inner.EnsureCurrentLineageAsync(lineageId, generation, cancellationToken);
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => Unavailable ? throw new SystemMetadataUnavailableException("synthetic System outage") : Inner.ReadLineageAsync(cancellationToken);
        public Task<SystemLineageMetadata> AdvanceGenerationAsync(Guid lineageId, long expectedPriorGeneration, long nextGeneration, CancellationToken cancellationToken = default) =>
            Unavailable ? throw new SystemMetadataUnavailableException("synthetic System outage") : fixture.FailSystemOperation == "advance" ? throw new IOException("synthetic generation advance crash") : Inner.AdvanceGenerationAsync(lineageId, expectedPriorGeneration, nextGeneration, cancellationToken);
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) =>
            Unavailable ? throw new SystemMetadataUnavailableException("synthetic System outage") : fixture.FailSystemOperation == "join" ? throw new IOException("synthetic membership publication crash") : Inner.JoinCurrentGenerationAsync(deviceId, displayName, cancellationToken);
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Unavailable ? throw new SystemMetadataUnavailableException("synthetic System outage") : Inner.ListCurrentGenerationDevicesAsync(lineageId, generation, cancellationToken);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Unavailable ? throw new SystemMetadataUnavailableException("synthetic System outage") : Inner.FindValidatedReadOnlySeedAsync(lineageId, generation, cancellationToken);
        public async Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            if (Unavailable) throw new SystemMetadataUnavailableException("synthetic System outage");
            if (fixture.FailSystemOperation == "publish-seed") throw new IOException("synthetic optional seed failure");
            PublishCount++;
            return await Inner.PublishReadOnlySeedAsync(metadata, payload, cancellationToken);
        }
    }

    private sealed class SnapshotRecorder(ServicePaths paths) : ILocalRecoverySnapshotService
    {
        public int CreateCalls { get; private set; }
        public string? CapturedDatabaseHash { get; private set; }
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return CaptureAsync(change, cancellationToken);
        }

        private async Task<RecoverySnapshotResult> CaptureAsync(DurableChange change, CancellationToken cancellationToken)
        {
            if (File.Exists(paths.LiveDatabasePath))
                CapturedDatabaseHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(paths.LiveDatabasePath, cancellationToken)));
            return new RecoverySnapshotResult(string.Empty, string.Empty, new string('A', 64), change.CommittedAtUtc, change.Sequence, 1);
        }
    }

    private sealed class UnsupportedSnapshotFactory : ITransferSnapshotFactory
    {
        public Task<TransferSnapshot> CreateAsync(AuthorityProtocolState source, Guid transferId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The stale device must never start a source transfer.");
    }

    private sealed class ActivationTransport : IGitHubHandoffTransport
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private readonly GitHubReleaseContainer release = new(301, "sushi81-handoff-v1", "https://uploads.example/releases/301/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextId = 5000;

        public string? EnsureFailure { get; set; }
        public bool UnknownAfterUpload { get; set; }
        public bool OtherWinnerOnUpload { get; set; }
        public bool UnknownOutcomeWasReobserved { get; private set; }
        public int UploadCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }
        public GitHubRemoteAsset[] ActivationAssets => assets.Where(asset => GitHubHandoffAssetNames.IsActivationName(asset.Name)).ToArray();
        public IReadOnlyList<string> AssetFingerprints => assets
            .OrderBy(asset => asset.Id)
            .Select(asset => $"{asset.Id}|{asset.Name}|{asset.Size}|{asset.State}|{asset.Digest}")
            .ToArray();

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default)
        {
            if (EnsureFailure is not null) throw CreateFailure(EnsureFailure);
            return Task.FromResult(release);
        }

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            if (OtherWinnerOnUpload)
            {
                var artifact = JsonSerializer.Deserialize<RecoveryActivationArtifact>(bytes, JsonOptions)! with { WinnerDeviceId = Guid.NewGuid() };
                bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            }
            var id = nextId++;
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, "uploaded", "sha256:" + hash, DateTimeOffset.UtcNow));
            contents[id] = bytes;
            if (UnknownAfterUpload)
            {
                UnknownAfterUpload = false;
                UnknownOutcomeWasReobserved = true;
                throw new TimeoutException("synthetic unknown create outcome after server commit");
            }
            return new GitHubAssetReceipt(release.Id, id, name, bytes.LongLength, "sha256:" + hash, DateTimeOffset.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());
        }

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(assets.Single(asset => asset.Id == assetId));
        }
        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            assets.RemoveAll(asset => asset.Id == assetId);
            contents.Remove(assetId);
            return Task.CompletedTask;
        }

        public void AddIncompleteStarter(Guid lineageId, long generation)
        {
            var name = GitHubHandoffAssetNames.CreateActivationName(lineageId, generation);
            AddAsset(nextId++, name, [1], "starter");
            if (assets.Count(asset => asset.Name == name) > 2) throw new InvalidOperationException();
        }

        public void AddCompleteOccupant(Guid lineageId, long generation)
        {
            var artifact = new RecoveryActivationArtifact("M07", Guid.NewGuid(), lineageId, generation - 1, generation, Guid.NewGuid(), "other-candidate", new string('A', 64), 8, DateTimeOffset.UtcNow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            AddAsset(nextId++, GitHubHandoffAssetNames.CreateActivationName(lineageId, generation), bytes, "uploaded");
        }

        public RemoteAssetEvidence AddHistoricalAsset(string name, byte[] bytes)
        {
            var id = nextId++;
            AddAsset(id, name, bytes, "uploaded");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return new RemoteAssetEvidence(release.Id, id, name, bytes.LongLength, hash);
        }

        private void AddAsset(long id, string name, byte[] bytes, string state)
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, state, "sha256:" + hash, DateTimeOffset.UtcNow));
            contents[id] = bytes;
        }

        private static Exception CreateFailure(string failure) => failure switch
        {
            "401" => new GitHubTransportException("synthetic unauthorized", HttpStatusCode.Unauthorized),
            "403" => new GitHubTransportException("synthetic forbidden", HttpStatusCode.Forbidden),
            "404" => new GitHubTransportException("synthetic missing", HttpStatusCode.NotFound),
            "5xx" => new GitHubTransportException("synthetic server error", HttpStatusCode.BadGateway),
            "timeout" => new TimeoutException("synthetic timeout"),
            _ => new HttpRequestException("synthetic offline")
        };
    }

    private sealed class ServicePaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 7);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }
}
