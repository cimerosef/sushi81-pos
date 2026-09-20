using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Desktop;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M00M10MicroEvidenceTests
{
    [TestMethod]
    public void CatalogueImportModeDialogReturnsEachAcceptedAndCancelledOutcomeOnRealSta()
    {
        RunOnSta(() =>
        {
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var localized = shell.Localized;

            var updateDialog = new CatalogueImportModeDialog(null!, localized) { ShowInTaskbar = false };
            Exception? updateFailure = null;
            updateDialog.ContentRendered += (_, _) =>
            {
                try
                {
                    GetPrivateField<RadioButton>(updateDialog, "update").IsChecked = true;
                    FindButtonByContent(updateDialog, localized["Continue"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception)
                {
                    updateFailure = exception;
                    updateDialog.Close();
                }
            };
            var updateResult = updateDialog.ShowDialog();
            if (updateFailure is not null) ExceptionDispatchInfo.Capture(updateFailure).Throw();
            Assert.IsTrue(updateResult.GetValueOrDefault());
            Assert.AreEqual(CatalogueImportMode.Update, updateDialog.SelectedMode);

            var addOnlyDialog = new CatalogueImportModeDialog(null!, localized) { ShowInTaskbar = false };
            Exception? addOnlyFailure = null;
            addOnlyDialog.ContentRendered += (_, _) =>
            {
                try
                {
                    GetPrivateField<RadioButton>(addOnlyDialog, "addOnly").IsChecked = true;
                    FindButtonByContent(addOnlyDialog, localized["Continue"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception)
                {
                    addOnlyFailure = exception;
                    addOnlyDialog.Close();
                }
            };
            var addOnlyResult = addOnlyDialog.ShowDialog();
            if (addOnlyFailure is not null) ExceptionDispatchInfo.Capture(addOnlyFailure).Throw();
            Assert.IsTrue(addOnlyResult.GetValueOrDefault());
            Assert.AreEqual(CatalogueImportMode.AddOnly, addOnlyDialog.SelectedMode);

            var cancelDialog = new CatalogueImportModeDialog(null!, localized) { ShowInTaskbar = false };
            Exception? cancelFailure = null;
            cancelDialog.ContentRendered += (_, _) =>
            {
                try { FindButtonByContent(cancelDialog, localized["Cancel"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                catch (Exception exception) { cancelFailure = exception; cancelDialog.Close(); }
            };
            var cancelResult = cancelDialog.ShowDialog();
            if (cancelFailure is not null) ExceptionDispatchInfo.Capture(cancelFailure).Throw();
            Assert.AreNotEqual(true, cancelResult);
            Assert.IsNull(cancelDialog.SelectedMode);

            var escapeDialog = new CatalogueImportModeDialog(null!, localized) { ShowInTaskbar = false };
            Exception? escapeFailure = null;
            escapeDialog.ContentRendered += (_, _) =>
            {
                try { RaiseEscape(escapeDialog); }
                catch (Exception exception) { escapeFailure = exception; escapeDialog.Close(); }
            };
            var escapeResult = escapeDialog.ShowDialog();
            if (escapeFailure is not null) ExceptionDispatchInfo.Capture(escapeFailure).Throw();
            Assert.AreNotEqual(true, escapeResult);
            Assert.IsNull(escapeDialog.SelectedMode);

            var closeDialog = new CatalogueImportModeDialog(null!, localized) { ShowInTaskbar = false };
            Exception? closeFailure = null;
            closeDialog.ContentRendered += (_, _) =>
            {
                try
                {
                    GetPrivateField<RadioButton>(closeDialog, "update").IsChecked = true;
                    closeDialog.Close();
                }
                catch (Exception exception) { closeFailure = exception; closeDialog.Close(); }
            };
            var closeResult = closeDialog.ShowDialog();
            if (closeFailure is not null) ExceptionDispatchInfo.Capture(closeFailure).Throw();
            Assert.AreNotEqual(true, closeResult);
            Assert.IsNull(closeDialog.SelectedMode);
        });
    }

    private static Button FindButtonByContent(Window dialog, string content) =>
        GetVisualDescendants<Button>(dialog).Single(button => string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal));

    private static void RaiseEscape(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        Assert.IsNotNull(source, "The real STA dialog must have a presentation source before key input.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        window.RaiseEvent(args);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Missing private field {fieldName}.");
        return (T)field!.GetValue(instance)!;
    }

    private static IEnumerable<T> GetVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) yield return typed;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(root); }
        catch (InvalidOperationException) { yield break; }
        for (var index = 0; index < count; index++)
        {
            foreach (var descendant in GetVisualDescendants<T>(VisualTreeHelper.GetChild(root, index))) yield return descendant;
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ResetStaleWpfApplication();
                action();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void ResetStaleWpfApplication()
    {
        var current = System.Windows.Application.Current;
        if (current is null || !current.Dispatcher.HasShutdownFinished) return;
        var field = typeof(System.Windows.Application).GetField("_appInstance", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }
}
