using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Desktop;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M07DisasterRecoveryUiTests
{
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
            dialog.Show();
            try
            {
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
            finally
            {
                dialog.Close();
            }

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
            pending.Show();
            try
            {
                pending.UpdateLayout();
                var quarantine = (CheckBox)pendingType.GetField("quarantineCheckBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
                var retry = (Button)pendingType.GetField("retryButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pending)!;
                Assert.IsFalse(retry.IsEnabled, "Pending recovery cannot resume without quarantine confirmation.");
                quarantine.IsChecked = true;
                Assert.IsTrue(retry.IsEnabled, "Pending recovery enables only exact retry after quarantine confirmation.");
                Assert.IsFalse(VisualDescendants<ComboBox>(pending).Any(), "Pending recovery must not expose candidate retargeting.");
            }
            finally
            {
                pending.Close();
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
