using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sushi81.Pos.Desktop;

/// <summary>Reusable operator-friendly behavior for numeric TextBox fields.</summary>
public static class NumericInputBehavior
{
    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllOnFocus",
            typeof(bool),
            typeof(NumericInputBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static void SetSelectAllOnFocus(DependencyObject element, bool value) =>
        element.SetValue(SelectAllOnFocusProperty, value);

    public static bool GetSelectAllOnFocus(DependencyObject element) =>
        (bool)element.GetValue(SelectAllOnFocusProperty);

    private static void OnSelectAllOnFocusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not TextBox textBox) return;
        if ((bool)args.NewValue)
        {
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
        else
        {
            textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is not TextBox textBox) return;
        textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(textBox.SelectAll));
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin) return;
        args.Handled = true;
        textBox.Focus();
        textBox.SelectAll();
    }
}
