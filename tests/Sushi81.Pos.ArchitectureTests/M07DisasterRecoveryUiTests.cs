using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M07DisasterRecoveryUiTests
{
    private static readonly string[] ResultVectors = ["LostToExistingWinner", "offline", "401", "timeout", "BlockedNoWinner"];
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
                (AuthorityPhase.ReleasedNonAuthoritative, "Disaster Recovery", "M07DisasterRecovery"),
                (AuthorityPhase.RelinquishedPendingGrant, "Disaster Recovery", "M07DisasterRecovery"),
                (AuthorityPhase.DisasterRecoveryPending, "Retry Disaster Recovery", "M07DisasterRecoveryPending"),
                (AuthorityPhase.DisasterRecoveryPreparing, "Retry Disaster Recovery", "M07DisasterRecoveryPending"),
                (AuthorityPhase.StaleGeneration, "Reinitialize stale device", "M07ReinitializeStale"),
                (AuthorityPhase.Authoritative, string.Empty, string.Empty),
                (AuthorityPhase.ClosedRetainedAuthority, string.Empty, string.Empty),
                (AuthorityPhase.TransferPreparing, string.Empty, string.Empty),
                (AuthorityPhase.TargetAcquisitionPending, string.Empty, string.Empty),
                (AuthorityPhase.RecoveryRequired, string.Empty, string.Empty)
            })
            {
                var writable = vector.Item1 is AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority;
                var transitioning = vector.Item1 is AuthorityPhase.TransferPreparing or AuthorityPhase.TargetAcquisitionPending
                    or AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending;
                using var guard = new WriteAuthorityGuard(writable
                    ? WriteAuthorityState.Authoritative
                    : transitioning ? WriteAuthorityState.Transitioning : WriteAuthorityState.NonAuthoritativeReadOnly);
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

                    var buttons = VisualDescendants<Button>(window)
                        .Where(item => AutomationProperties.GetName(item) is "Disaster Recovery" or "Retry Disaster Recovery" or "Reinitialize stale device")
                        .ToArray();
                    Assert.HasCount(3, buttons, vector.Item1.ToString());
                    var expectedVisible = vector.Item2;
                    foreach (var button in buttons)
                    {
                        var shouldShow = AutomationProperties.GetName(button) == expectedVisible;
                        Assert.AreEqual(shouldShow ? Visibility.Visible : Visibility.Collapsed, button.Visibility, vector.Item1.ToString());
                        Assert.AreEqual(shouldShow, button.IsEnabled, vector.Item1.ToString());
                    }
                    if (expectedVisible.Length > 0)
                    {
                        var button = buttons.Single(item => AutomationProperties.GetName(item) == expectedVisible);
                        Assert.AreEqual(shell.Localized[vector.Item3], button.Content);
                    }
                    Assert.AreEqual(writable, shell.CanWrite);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(shell.AuthorityStatus));

                    if (vector.Item1 is AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly)
                    {
                        shell.RefreshAuthorityStateAsync().GetAwaiter().GetResult();
                        window.UpdateLayout();
                        var refreshed = buttons.Single(item => AutomationProperties.GetName(item) == expectedVisible);
                        Assert.AreEqual(Visibility.Visible, refreshed.Visibility, "M07 refresh changed the shown action state.");
                        Assert.IsTrue(refreshed.IsEnabled, "M07 refresh disabled the shown action state.");
                    }

                    shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN"))
                        .GetAwaiter().GetResult();
                    window.UpdateLayout();
                    if (expectedVisible.Length > 0)
                    {
                        var localizedButton = buttons.Single(item => AutomationProperties.GetName(item) == expectedVisible);
                        Assert.AreEqual(shell.Localized[vector.Item3], localizedButton.Content, "The shown M07 surface did not localize.");
                        Assert.AreEqual(Visibility.Visible, localizedButton.Visibility, "Localization changed the shown action visibility.");
                        Assert.IsTrue(localizedButton.IsEnabled, "Localization changed the shown action state.");
                    }
                }
                finally
                {
                    window.Hide();
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
                StringAssert.Contains(text, "orientation-only");
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
    public void PreparingAndPendingRecoverySurfacesShowExactPersistedIdentityOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var phase in new[] { AuthorityPhase.DisasterRecoveryPreparing, AuthorityPhase.DisasterRecoveryPending })
            {
                var recoveryId = Guid.NewGuid();
                var context = new DisasterRecoveryEntryContext(
                    phase, Guid.NewGuid(), 7, Guid.NewGuid(), null, null, null, null,
                    DisasterRecoveryEntryReason.NormalAuthorityUnavailable, true, recoveryId,
                    "candidate-exact", "OneDriveCheckpoint", 44, 21);
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
                    StringAssert.Contains(text, recoveryId.ToString("N")[..8], phase.ToString());
                    StringAssert.Contains(text, "candidate-exact", phase.ToString());
                    StringAssert.Contains(text, "44", phase.ToString());
                    StringAssert.Contains(text, "21", phase.ToString());
                    StringAssert.Contains(text, "7 → 8", phase.ToString());
                    Assert.IsFalse(LogicalDescendants<ComboBox>(pending).Any(), phase.ToString());
                    CollectionAssert.DoesNotContain(
                        LogicalDescendants<Button>(pending).Select(button => button.Content as string).Where(value => value is not null).ToArray(),
                        "Cancel");
                }
                finally { pending.Close(); }
            }
        });
    }

    [TestMethod]
    public void DisasterRecoveryDialogsRenderFrenchAndChineseWithoutChangingIdentityOnSta()
    {
        RunOnSta(() =>
        {
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true);
            var lineage = Guid.NewGuid();
            var source = Guid.NewGuid();
            var target = Guid.NewGuid();
            var recoveryId = Guid.NewGuid();
            var context = new DisasterRecoveryEntryContext(
                AuthorityPhase.ReleasedNonAuthoritative, lineage, 4, Guid.NewGuid(), source, target,
                Guid.NewGuid(), 19, DisasterRecoveryEntryReason.ReleasedTargetUnavailable, true, recoveryId,
                "candidate-language", "OneDriveCheckpoint", 31, 19);
            var candidate = new RecoveryCandidate(
                RecoveryCandidateType.OneDriveCheckpoint, "candidate-language", lineage, 4, source, 31, 19,
                DateTimeOffset.UtcNow, 128, new string('A', 64), "C:\\synthetic\\seed.db");

            var frFirst = ShowFirstUseDialogText(shell.Localized, context, candidate);
            var frPending = ShowPendingDialogText(shell.Localized, context);
            shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
            var zhFirst = ShowFirstUseDialogText(shell.Localized, context, candidate);
            var zhPending = ShowPendingDialogText(shell.Localized, context);

            foreach (var text in new[] { frFirst, zhFirst })
            {
                StringAssert.Contains(text, "4");
                StringAssert.Contains(text, "19");
            }
            foreach (var text in new[] { frPending, zhPending })
            {
                StringAssert.Contains(text, recoveryId.ToString("N")[..8]);
                StringAssert.Contains(text, "candidate-language");
                StringAssert.Contains(text, "4");
                StringAssert.Contains(text, "19");
            }
            Assert.AreNotEqual(frFirst, zhFirst);
            Assert.AreNotEqual(frPending, zhPending);
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

    internal static void AssertShownFailClosedResultsAcrossTheM07SafetyMatrixOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var vector in ResultVectors)
            {
                var root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.UI.Results", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var paths = new UiPaths(root);
                var lineage = Guid.NewGuid();
                var candidate = CreateUiCandidate(root, lineage);
                var discovery = new UiCandidateDiscovery(candidate);
                var transport = new UiTransport
                {
                    OtherWinnerOnUpload = vector == "LostToExistingWinner",
                    EnsureFailure = vector == "offline" || vector == "401" || vector == "timeout" ? vector : null
                };
                if (vector == "BlockedNoWinner")
                {
                    transport.AddIncompleteStarter(candidate.LineageId, candidate.Generation + 1);
                    transport.AddIncompleteStarter(candidate.LineageId, candidate.Generation + 1);
                }

                using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
                var runtime = CreateShownRuntime(guard, AuthorityPhase.NonAuthoritativeReadOnly, discovery, transport, paths, lineage);
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(), true, authorityGuard: guard,
                    authorityState: guard.State, m07Runtime: runtime, authorityPhase: AuthorityPhase.NonAuthoritativeReadOnly);
                var result = shell.StartDisasterRecoveryAsync(candidate.CandidateId, true, true).GetAwaiter().GetResult();
                Assert.IsNotNull(result, vector);
                Assert.IsFalse(result!.Succeeded, vector);
                Assert.IsFalse(shell.CanWrite, vector);
                Assert.AreNotEqual(WriteAuthorityState.Authoritative, guard.State, vector);
                Assert.IsFalse(string.IsNullOrWhiteSpace(shell.M07OperationStatus), vector);

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
                    Assert.IsTrue(LogicalDescendants<TextBlock>(window).Any(text => text.Text == shell.M07OperationStatus), vector);
                    shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                    window.UpdateLayout();
                    Assert.IsFalse(string.IsNullOrWhiteSpace(shell.M07OperationStatus), vector);
                    Assert.IsFalse(shell.CanWrite, vector);
                }
                finally
                {
                    window.Hide();
                    runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
            }

            using var staleGuard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            awaitableRuntime(staleGuard, AuthorityPhase.StaleGeneration, out var staleRuntime);
            using var staleShell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true, authorityGuard: staleGuard,
                authorityState: staleGuard.State, m07Runtime: staleRuntime, authorityPhase: AuthorityPhase.StaleGeneration);
            var staleWindow = new MainWindow(staleShell) { Width = 980, Height = 680, ShowInTaskbar = false };
            try
            {
                staleWindow.Show();
                staleWindow.UpdateLayout();
                Assert.IsTrue(LogicalDescendants<TextBlock>(staleWindow).Any(text => text.Text == staleShell.M07OperationStatus));
                Assert.IsFalse(staleShell.CanWrite);
            }
            finally
            {
                staleWindow.Hide();
                staleRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
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

    private static string ShowFirstUseDialogText(
        IReadOnlyDictionary<string, string> labels,
        DisasterRecoveryEntryContext context,
        RecoveryCandidate candidate)
    {
        var dialogType = typeof(MainWindow).GetNestedType("DisasterRecoveryDialog", BindingFlags.NonPublic)!;
        var constructor = dialogType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [typeof(Window), typeof(IReadOnlyDictionary<string, string>), typeof(DisasterRecoveryEntryContext), typeof(IReadOnlyList<RecoveryCandidate>), typeof(RecoveryCandidate)], null)!;
        var dialog = (Window)constructor.Invoke([null, labels, context, (IReadOnlyList<RecoveryCandidate>)[candidate], candidate]);
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            return string.Join("\n", LogicalDescendants<TextBlock>(dialog).Select(block => block.Text))
                + "\n" + string.Join("\n", VisualDescendants<ComboBox>(dialog).SelectMany(combo => combo.Items.OfType<object>().Select(item => item.ToString())));
        }
        finally { dialog.Close(); }
    }

    private static string ShowPendingDialogText(
        IReadOnlyDictionary<string, string> labels,
        DisasterRecoveryEntryContext context)
    {
        var pendingType = typeof(MainWindow).GetNestedType("DisasterRecoveryPendingDialog", BindingFlags.NonPublic)!;
        var constructor = pendingType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [typeof(Window), typeof(IReadOnlyDictionary<string, string>), typeof(DisasterRecoveryEntryContext)], null)!;
        var pending = (Window)constructor.Invoke([null, labels, context]);
        try
        {
            pending.Show();
            pending.UpdateLayout();
            return string.Join("\n", LogicalDescendants<TextBlock>(pending).Select(block => block.Text));
        }
        finally { pending.Close(); }
    }

    internal static M07RuntimeServices CreateShownRuntime(
        WriteAuthorityGuard guard,
        AuthorityPhase phase,
        IRecoveryCandidateDiscovery? discovery = null,
        IGitHubHandoffTransport? transport = null,
        IAppPaths? paths = null,
        Guid? lineageOverride = null)
    {
        paths ??= new UiPaths(Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.UI", Guid.NewGuid().ToString("N")));
        discovery ??= new UiCandidateDiscovery();
        var store = new UiAuthorityStore(CreateRefreshDocument(phase, lineageOverride));
        var metadata = new UiSystemMetadataStore();
        var recovery = new DisasterRecoveryService(
            store, guard, metadata, discovery,
            transport is null ? null : new RecoveryActivationService(transport),
            transport, new UiSnapshotService(), paths, new UiClock());
        return new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new UiClock()), null, null, null,
            GitHubConnectionSetupState.RepositoryNotConfigured, recovery, discovery);
    }

    private static void awaitableRuntime(WriteAuthorityGuard guard, AuthorityPhase phase, out M07RuntimeServices runtime) =>
        runtime = CreateShownRuntime(guard, phase);

    private static AuthorityStateDocument? CreateRefreshDocument(AuthorityPhase phase, Guid? lineageOverride = null)
    {
        if (phase is not (AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly))
            return null;

        var deviceId = Guid.NewGuid();
        var protocol = phase == AuthorityPhase.PairedUninitializedReadOnly
            ? new AuthorityProtocolState(1, deviceId, "Shown UI", lineageOverride ?? Guid.NewGuid(), 1, 0, 0, phase)
            : new AuthorityProtocolState(1, deviceId, "Shown UI", lineageOverride ?? Guid.NewGuid(), 1, 0, 0, phase);
        return new AuthorityStateDocument(1, protocol.WriteState, DateTimeOffset.UtcNow) { Protocol = protocol };
    }

    private static RecoveryCandidate CreateUiCandidate(string root, Guid lineageId)
    {
        var path = Path.Combine(root, "candidate.db");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','8'); CREATE TABLE synthetic_markers(marker TEXT NOT NULL PRIMARY KEY);";
            command.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(path);
        return new RecoveryCandidate(
            RecoveryCandidateType.OneDriveCheckpoint,
            "ui-candidate",
            lineageId,
            1,
            Guid.NewGuid(),
            8,
            0,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            path);
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
        public void EnsureInitialized()
        {
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }
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
        private readonly List<RecoveryCandidate> candidates = [];

        public UiCandidateDiscovery() { }

        public UiCandidateDiscovery(RecoveryCandidate candidate) => candidates.Add(candidate);

        public Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default)
        {
            var ordered = candidates.OrderByDescending(candidate => candidate.BusinessRevision).ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal).ToArray();
            return Task.FromResult(new RecoveryCandidateDiscoveryResult(ordered, "synthetic UI discovery"));
        }

        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId));
    }

    private sealed class UiTransport : IGitHubHandoffTransport
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly GitHubReleaseContainer release = new(301, "sushi81-handoff-v1", "https://uploads.example/releases/301/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextId = 5000;

        public string? EnsureFailure { get; set; }
        public bool OtherWinnerOnUpload { get; set; }

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default)
        {
            if (EnsureFailure is not null) throw CreateFailure(EnsureFailure);
            return Task.FromResult(release);
        }

        public async Task<GitHubAssetReceipt> UploadAssetAsync(
            GitHubReleaseContainer release,
            string name,
            Stream content,
            long contentLength,
            string localSha256,
            CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            if (OtherWinnerOnUpload)
            {
                var artifact = JsonSerializer.Deserialize<RecoveryActivationArtifact>(bytes, JsonOptions)! with { WinnerDeviceId = Guid.NewGuid() };
                bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            }
            var id = nextId++;
            AddAsset(id, name, bytes, "uploaded");
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new GitHubAssetReceipt(release.Id, id, name, bytes.LongLength, digest, DateTimeOffset.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(assets.Single(asset => asset.Id == assetId));

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            assets.RemoveAll(asset => asset.Id == assetId);
            contents.Remove(assetId);
            return Task.CompletedTask;
        }

        public void AddIncompleteStarter(Guid lineageId, long generation) =>
            AddAsset(nextId++, GitHubHandoffAssetNames.CreateActivationName(lineageId, generation), [1], "starter");

        private void AddAsset(long id, string name, byte[] bytes, string state)
        {
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, state, "sha256:" + digest, DateTimeOffset.UtcNow));
            contents[id] = bytes;
        }

        private static Exception CreateFailure(string failure) => failure switch
        {
            "401" => new GitHubTransportException("synthetic unauthorized", HttpStatusCode.Unauthorized),
            "timeout" => new TimeoutException("synthetic timeout"),
            _ => new HttpRequestException("synthetic offline")
        };
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
