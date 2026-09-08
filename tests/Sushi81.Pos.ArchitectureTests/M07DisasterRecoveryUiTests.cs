using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M07DisasterRecoveryUiTests
{
    private static readonly string[] LocalizationKeys =
        ["M07DisasterRecovery", "M07ContextGeneration", "M07RetrySameRecovery", "M07NormalPathUnavailableConfirm", "M07CandidateRecommended"];

    [TestMethod]
    public void DisasterRecoveryShellActionsFollowEligiblePhaseOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var phase in new[]
            {
                AuthorityPhase.PairedUninitializedReadOnly,
                AuthorityPhase.NonAuthoritativeReadOnly,
                AuthorityPhase.DisasterRecoveryPending,
                AuthorityPhase.StaleGeneration,
                AuthorityPhase.Authoritative
            })
            {
                using var guard = new WriteAuthorityGuard(phase == AuthorityPhase.Authoritative
                    ? WriteAuthorityState.Authoritative
                    : WriteAuthorityState.NonAuthoritativeReadOnly);
                M07RuntimeServices? runtime = null;
                if (phase != AuthorityPhase.Authoritative)
                    awaitableRuntime(guard, phase, out runtime);
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(), true, authorityGuard: guard,
                    authorityState: guard.State, m07Runtime: runtime, authorityPhase: phase);
                Assert.AreEqual(phase is AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly, shell.CanStartDisasterRecovery, phase.ToString());
                Assert.AreEqual(phase is AuthorityPhase.DisasterRecoveryPending, shell.CanRetryDisasterRecovery, phase.ToString());
                Assert.AreEqual(phase is AuthorityPhase.StaleGeneration, shell.CanReinitializeStaleDevice, phase.ToString());
                runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    internal static void AssertShownMainWindowPreservesM07ActionStateAcrossRefreshAndLocalizationOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var vector in new[]
            {
                (AuthorityPhase.PairedUninitializedReadOnly, "Disaster Recovery", "M07DisasterRecovery"),
                (AuthorityPhase.NonAuthoritativeReadOnly, "Disaster Recovery", "M07DisasterRecovery"),
                (AuthorityPhase.DisasterRecoveryPending, "Retry Disaster Recovery", "M07DisasterRecoveryPending"),
                (AuthorityPhase.StaleGeneration, "Reinitialize stale device", "M07ReinitializeStale")
            })
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
                awaitableRuntime(guard, vector.Item1, out var runtime);
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(), true, authorityGuard: guard,
                    authorityState: guard.State, m07Runtime: runtime, authorityPhase: vector.Item1);
                var window = new MainWindow(shell)
                {
                    Width = 980,
                    Height = 680,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 0,
                    Top = 0
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                    var button = VisualDescendants<Button>(window)
                        .Single(item => AutomationProperties.GetName(item) == vector.Item2);
                    Assert.AreEqual(Visibility.Visible, button.Visibility, vector.Item1.ToString());
                    Assert.IsTrue(button.IsEnabled, vector.Item1.ToString());
                    Assert.AreEqual(shell.Localized[vector.Item3], button.Content);
                    Assert.IsFalse(shell.CanWrite);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(shell.AuthorityStatus));

                    if (vector.Item1 is AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly)
                    {
                        shell.RefreshAuthorityStateAsync().GetAwaiter().GetResult();
                        window.UpdateLayout();
                        Assert.AreEqual(Visibility.Visible, button.Visibility, "M07 refresh changed the shown action state.");
                        Assert.IsTrue(button.IsEnabled, "M07 refresh disabled the shown action state.");
                    }

                    shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN"))
                        .GetAwaiter().GetResult();
                    window.UpdateLayout();
                    Assert.AreEqual(shell.Localized[vector.Item3], button.Content, "The shown M07 surface did not localize.");
                    Assert.AreEqual(Visibility.Visible, button.Visibility, "Localization changed the shown action visibility.");
                    Assert.IsTrue(button.IsEnabled, "Localization changed the shown action state.");
                }
                finally
                {
                    window.Close();
                    runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
        });
    }

    [TestMethod]
    public void DisasterRecoveryDialogRendersContextFreshnessAndReadOnlyCandidateOnSta()
    {
        RunOnSta(() =>
        {
            var lineage = Guid.NewGuid();
            var source = Guid.NewGuid();
            var target = Guid.NewGuid();
            var context = new DisasterRecoveryEntryContext(
                AuthorityPhase.ReleasedNonAuthoritative, lineage, 4, Guid.NewGuid(), source, target,
                Guid.NewGuid(), 19, DisasterRecoveryEntryReason.ReleasedTargetUnavailable, true);
            var candidate = new RecoveryCandidate(
                RecoveryCandidateType.OneDriveCheckpoint, "recommended-a", lineage, 4, source, 31, 19,
                DateTimeOffset.UtcNow, 128, new string('A', 64), "C:\\synthetic\\seed.db");
            var dialogType = typeof(MainWindow).GetNestedType("DisasterRecoveryDialog", BindingFlags.NonPublic)!;
            var constructor = dialogType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [typeof(Window), typeof(IReadOnlyDictionary<string, string>), typeof(DisasterRecoveryEntryContext), typeof(IReadOnlyList<RecoveryCandidate>), typeof(RecoveryCandidate)], null)!;
            var dialog = (Window)constructor.Invoke([null, new Dictionary<string, string>(), context, (IReadOnlyList<RecoveryCandidate>)[candidate], candidate]);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var text = string.Join("\n", LogicalDescendants<TextBlock>(dialog).Select(block => block.Text));
                StringAssert.Contains(text, "ReleasedNonAuthoritative");
                StringAssert.Contains(text, lineage.ToString("N")[..8]);
                StringAssert.Contains(text, "4 → 5");
                StringAssert.Contains(text, source.ToString("N")[..8]);
                StringAssert.Contains(text, target.ToString("N")[..8]);
                StringAssert.Contains(text, "19");
                var selector = (ComboBox)dialogType.GetField("candidateSelector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Assert.IsFalse(selector.IsEnabled);
                var selectedChoice = selector.SelectedItem!;
                Assert.AreEqual(candidate.CandidateId, selectedChoice.GetType().GetProperty("CandidateId")!.GetValue(selectedChoice));
                StringAssert.Contains(selector.SelectedItem!.ToString()!, "Protocol-selected");
            }
            finally { dialog.Close(); }
        });
    }

    [TestMethod]
    public void PendingRecoverySurfaceRendersExactIdentityWithoutRetargetOrProtocolCancelOnSta()
    {
        RunOnSta(() =>
        {
            var context = new DisasterRecoveryEntryContext(
                AuthorityPhase.DisasterRecoveryPending, Guid.NewGuid(), 4, Guid.NewGuid(), null, null, null, null,
                DisasterRecoveryEntryReason.NormalAuthorityUnavailable, true, Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "recommended-a", "OneDriveCheckpoint", 31, 19);
            var pendingType = typeof(MainWindow).GetNestedType("DisasterRecoveryPendingDialog", BindingFlags.NonPublic)!;
            var constructor = pendingType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [typeof(Window), typeof(IReadOnlyDictionary<string, string>), typeof(DisasterRecoveryEntryContext)], null)!;
            var pending = (Window)constructor.Invoke([null, new Dictionary<string, string>(), context]);
            try
            {
                pending.Show();
                pending.UpdateLayout();
                var text = string.Join("\n", LogicalDescendants<TextBlock>(pending).Select(block => block.Text));
                StringAssert.Contains(text, "11111111");
                StringAssert.Contains(text, "recommended-a");
                StringAssert.Contains(text, "31");
                StringAssert.Contains(text, "19");
                StringAssert.Contains(text, "4 → 5");
                var buttons = LogicalDescendants<Button>(pending).Select(button => button.Content as string).Where(value => value is not null).ToArray();
                CollectionAssert.Contains(buttons, "Retry same recovery");
                CollectionAssert.Contains(buttons, "Close");
                CollectionAssert.DoesNotContain(buttons, "Cancel");
                Assert.IsFalse(LogicalDescendants<ComboBox>(pending).Any());
            }
            finally { pending.Close(); }
        });
    }

    [TestMethod]
    public void DisasterRecoveryStringsRoundTripFrenchAndChineseOnSta()
    {
        RunOnSta(() =>
        {
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true);
            foreach (var key in LocalizationKeys) Assert.IsFalse(string.IsNullOrWhiteSpace(shell.Localized[key]), key);
            shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
            foreach (var key in LocalizationKeys) Assert.IsFalse(string.IsNullOrWhiteSpace(shell.Localized[key]), key);
            shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
            foreach (var key in LocalizationKeys) Assert.IsFalse(string.IsNullOrWhiteSpace(shell.Localized[key]), key);
        });
    }

    [TestMethod]
    public void DisasterRecoveryDialogsExposeFailClosedConfirmationAndExactResumeOnSta()
    {
        RunOnSta(() =>
        {
            var context = new DisasterRecoveryEntryContext(
                AuthorityPhase.ReleasedNonAuthoritative,
                Guid.NewGuid(),
                4,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                19,
                DisasterRecoveryEntryReason.ReleasedTargetUnavailable,
                NormalPathUnavailableConfirmationRequired: true);
            var candidate = new RecoveryCandidate(
                RecoveryCandidateType.OneDriveCheckpoint,
                "system-seed:current:h19",
                context.LineageId!.Value,
                context.Generation,
                context.PriorSourceDeviceId!.Value,
                31,
                19,
                DateTimeOffset.UtcNow,
                128,
                new string('A', 64),
                "C:\\synthetic\\seed.db");
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            var dialogType = typeof(MainWindow).GetNestedType("DisasterRecoveryDialog", BindingFlags.NonPublic)!;
            var dialogConstructor = dialogType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [typeof(Window), typeof(IReadOnlyDictionary<string, string>), typeof(DisasterRecoveryEntryContext), typeof(IReadOnlyList<RecoveryCandidate>), typeof(RecoveryCandidate)],
                null)!;
            var dialog = (Window)dialogConstructor.Invoke([null, labels, context, (IReadOnlyList<RecoveryCandidate>)[candidate], candidate]);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var selector = (ComboBox)dialogType.GetField("candidateSelector", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var normalPath = (CheckBox)dialogType.GetField("normalPathUnavailableCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var quarantine = (CheckBox)dialogType.GetField("quarantineCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var confirm = (Button)dialogType.GetField("confirmButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

                Assert.IsFalse(selector.IsEnabled, "The candidate list is read-only diagnostics, not an operator retarget control.");
                Assert.IsFalse(confirm.IsEnabled, "Recovery must be fail-closed before both confirmations.");
                normalPath.IsChecked = true;
                Assert.IsFalse(confirm.IsEnabled, "Quarantine confirmation remains independently required.");
                quarantine.IsChecked = true;
                Assert.IsTrue(confirm.IsEnabled, "Only both explicit confirmations enable recovery.");
                var selectedCandidateId = (string?)dialogType.GetProperty("SelectedCandidateId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(dialog);
                Assert.AreEqual(candidate.CandidateId, selectedCandidateId);
            }
            finally { dialog.Close(); }

            var pendingType = typeof(MainWindow).GetNestedType("DisasterRecoveryPendingDialog", BindingFlags.NonPublic)!;
            var pendingConstructor = pendingType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [typeof(Window), typeof(IReadOnlyDictionary<string, string>), typeof(DisasterRecoveryEntryContext)],
                null)!;
            var pending = (Window)pendingConstructor.Invoke([null, labels, context with
            {
                RecoveryId = Guid.NewGuid(),
                RecoveryCandidateId = candidate.CandidateId,
                RecoveryCandidateType = candidate.TypeName,
                RecoveryBusinessRevision = candidate.BusinessRevision,
                RecoveryHandoffVersion = candidate.HandoffVersion
            }]);
            try
            {
                pending.Show();
                pending.UpdateLayout();
                var quarantine = (CheckBox)pendingType.GetField("quarantineCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
                var retry = (Button)pendingType.GetField("retryButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
                Assert.IsFalse(retry.IsEnabled, "Pending recovery cannot resume without quarantine confirmation.");
                quarantine.IsChecked = true;
                Assert.IsTrue(retry.IsEnabled, "Pending recovery enables only exact retry after quarantine confirmation.");
                Assert.IsFalse(VisualDescendants<ComboBox>(pending).Any(), "Pending recovery must not expose candidate retargeting.");
            }
            finally { pending.Close(); }
        });
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
        }
    }

    private static void awaitableRuntime(WriteAuthorityGuard guard, AuthorityPhase phase, out M07RuntimeServices runtime)
    {
        var paths = new UiPaths(Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.UI", Guid.NewGuid().ToString("N")));
        var store = new UiAuthorityStore(CreateRefreshDocument(phase));
        var metadata = new UiSystemMetadataStore();
        var discovery = new UiCandidateDiscovery();
        var recovery = new DisasterRecoveryService(
            store, guard, metadata, discovery, null, null, new UiSnapshotService(), paths, new UiClock());
        runtime = new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new UiClock()), null, null, null,
            GitHubConnectionSetupState.RepositoryNotConfigured, recovery, discovery);
    }

    private static AuthorityStateDocument? CreateRefreshDocument(AuthorityPhase phase)
    {
        if (phase is not (AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly))
            return null;

        var deviceId = Guid.NewGuid();
        var protocol = phase == AuthorityPhase.PairedUninitializedReadOnly
            ? new AuthorityProtocolState(1, deviceId, "Shown UI", Guid.NewGuid(), 1, 0, 0, phase)
            : new AuthorityProtocolState(1, deviceId, "Shown UI", null, 0, 0, 0, phase);
        return new AuthorityStateDocument(1, protocol.WriteState, DateTimeOffset.UtcNow) { Protocol = protocol };
    }

    private sealed class UiClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 8);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class UiPaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");
        public void EnsureInitialized() => Directory.CreateDirectory(TempDirectory);
    }

    private sealed class UiAuthorityStore(AuthorityStateDocument? document = null) : IAuthorityStateStore
    {
        public Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(document);
        public Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UiSystemMetadataStore : ISystemMetadataStore
    {
        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceRegistrationArtifact>>([]);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromResult<ValidatedReadOnlySeed?>(null);
        public Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UiCandidateDiscovery : IRecoveryCandidateDiscovery
    {
        public Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default) => Task.FromResult(new RecoveryCandidateDiscoveryResult([], "synthetic UI discovery"));
        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default) => Task.FromResult<RecoveryCandidate?>(null);
    }

    private sealed class UiSnapshotService : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) => Task.FromResult(new RecoverySnapshotResult(string.Empty, string.Empty, new string('A', 64), change.CommittedAtUtc, change.Sequence, 1));
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
