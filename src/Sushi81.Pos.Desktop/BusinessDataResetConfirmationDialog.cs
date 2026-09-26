using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Sushi81.Pos.Application.Maintenance;

namespace Sushi81.Pos.Desktop;

internal sealed class BusinessDataResetConfirmationDialog : Window
{
    private readonly IReadOnlyDictionary<string, string> text;
    private readonly BusinessDataResetPreview preview;
    private readonly TextBox confirmationToken = new();
    private readonly StackPanel contentPanel = new();

    public BusinessDataResetConfirmationDialog(
        Window owner,
        IReadOnlyDictionary<string, string> localizedText,
        BusinessDataResetPreview resetPreview)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        text = localizedText ?? throw new ArgumentNullException(nameof(localizedText));
        preview = resetPreview ?? throw new ArgumentNullException(nameof(resetPreview));

        Title = Text("BusinessDataResetPreviewTitle");
        Width = 560;
        MinHeight = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(22) };
        contentPanel.Orientation = Orientation.Vertical;
        DockPanel.SetDock(contentPanel, Dock.Top);
        root.Children.Add(contentPanel);
        Content = root;
        ShowPreviewStep();
    }

    public string ConfirmationToken { get; private set; } = string.Empty;

    internal TextBox ConfirmationTokenInput => confirmationToken;

    internal Button ContinueButton { get; private set; } = null!;

    internal Button FinalResetButton { get; private set; } = null!;

    internal Button CancelButton { get; private set; } = null!;

    internal bool IsFinalStep { get; private set; }

    internal bool IsResetConfirmed { get; private set; }

    private void ShowPreviewStep()
    {
        contentPanel.Children.Clear();
        contentPanel.Children.Add(new TextBlock
        {
            Text = string.Format(
                CultureInfo.CurrentCulture,
                Text("BusinessDataResetPreviewSummary"),
                preview.Orders,
                preview.Products,
                preview.Categories,
                preview.OptionGroups,
                preview.Options,
                preview.ExportBatches,
                preview.PreparedExportBatches,
                preview.AnnualArchiveRecords,
                preview.ActiveArchiveYears,
                preview.ActiveArchiveFiles,
                preview.OrderReferenceSequences),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = Text("BusinessDataResetPreserved"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = Text("BusinessDataResetTypePrompt"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        confirmationToken.Clear();
        confirmationToken.Margin = new Thickness(0, 0, 0, 16);
        AutomationProperties.SetName(confirmationToken, Text("BusinessDataResetTypePrompt"));
        contentPanel.Children.Add(confirmationToken);

        CancelButton = CreateButton(Text("Cancel"), CancelDialog);
        ContinueButton = CreateButton(Text("BusinessDataResetProceed"), ShowFinalStep);
        ContinueButton.IsEnabled = false;
        confirmationToken.TextChanged += (_, _) =>
            ContinueButton.IsEnabled = string.Equals(confirmationToken.Text, BusinessDataResetService.RequiredConfirmation, StringComparison.Ordinal);

        confirmationToken.Focus();
        contentPanel.Children.Add(CreateButtonRow(CancelButton, ContinueButton));
    }

    private void ShowFinalStep()
    {
        if (!string.Equals(confirmationToken.Text, BusinessDataResetService.RequiredConfirmation, StringComparison.Ordinal))
            return;

        IsFinalStep = true;
        ConfirmationToken = confirmationToken.Text;
        contentPanel.Children.Clear();
        contentPanel.Children.Add(new TextBlock
        {
            Text = Text("BusinessDataResetFinalConfirm"),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18)
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = Text("BusinessDataResetPreserved"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        CancelButton = CreateButton(Text("Cancel"), CancelDialog);
        FinalResetButton = CreateButton(Text("BusinessDataResetConfirmAndReset"), ConfirmReset);
        FinalResetButton.IsDefault = true;
        contentPanel.Children.Add(CreateButtonRow(CancelButton, FinalResetButton));
        FinalResetButton.Focus();
    }

    private void CancelDialog()
    {
        IsResetConfirmed = false;
        if (IsVisible)
            DialogResult = false;
    }

    private void ConfirmReset()
    {
        if (!IsFinalStep)
            return;

        IsResetConfirmed = true;
        if (IsVisible)
            DialogResult = true;
    }

    private static Button CreateButton(string label, Action click)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 88
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static StackPanel CreateButtonRow(Button cancel, Button primary)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        row.Children.Add(cancel);
        row.Children.Add(primary);
        return row;
    }

    private string Text(string key) => text.TryGetValue(key, out var value)
        ? value
        : throw new InvalidOperationException($"Missing required reset-dialog localization '{key}'.");
}
