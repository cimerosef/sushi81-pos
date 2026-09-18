using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Sushi81.Pos.Application.Catalogue;

namespace Sushi81.Pos.Desktop;

internal sealed class NativeCatalogueWorkbookFileDialogs : ICatalogueWorkbookFileDialogs
{
    public string? ShowSave(object owner, string suggestedFileName, string? filter = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? "Excel workbook (*.xlsx)|*.xlsx" : filter,
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName,
        };
        return dialog.ShowDialog(owner as Window) == true ? dialog.FileName : null;
    }

    public string? ShowOpen(object owner, string? filter = null)
    {
        var dialog = new OpenFileDialog
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? "Excel workbook (*.xlsx)|*.xlsx" : filter,
            DefaultExt = ".xlsx",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog(owner as Window) == true ? dialog.FileName : null;
    }
}

/// <summary>Explicit Update/Add-only choice. No radio button is selected initially.</summary>
internal sealed class CatalogueImportModeDialog : Window
{
    private readonly RadioButton update;
    private readonly RadioButton addOnly;
    private readonly Button continueButton;

    public CatalogueImportMode? SelectedMode { get; private set; }

    public CatalogueImportModeDialog(Window owner, IReadOnlyDictionary<string, string> localized)
    {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 520;
        MinWidth = 440;
        SizeToContent = SizeToContent.Height;
        Title = Read(localized, "CatalogueImportModeTitle", "Import Catalogue");
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = Read(localized, "CatalogueImportModePrompt", "Choose how the workbook will be applied."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        update = new RadioButton
        {
            Content = Read(localized, "CatalogueImportUpdateMode", "Update mode"),
            GroupName = "CatalogueImportMode",
            Margin = new Thickness(0, 0, 0, 4),
        };
        AutomationProperties.SetName(update, Read(localized, "CatalogueImportUpdateMode", "Update mode"));
        root.Children.Add(update);
        root.Children.Add(new TextBlock
        {
            Text = Read(localized, "CatalogueImportUpdateDescription", "Existing exported rows update by protected identity; blank-ID rows may create records; omitted rows are not deleted."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 12),
            Opacity = 0.82,
        });

        addOnly = new RadioButton
        {
            Content = Read(localized, "CatalogueImportAddOnlyMode", "Add-only mode"),
            GroupName = "CatalogueImportMode",
            Margin = new Thickness(0, 0, 0, 4),
        };
        AutomationProperties.SetName(addOnly, Read(localized, "CatalogueImportAddOnlyMode", "Add-only mode"));
        root.Children.Add(addOnly);
        root.Children.Add(new TextBlock
        {
            Text = Read(localized, "CatalogueImportAddOnlyDescription", "Create-only. Existing records are not updated; current Categories may be reused by name; Product-code collisions block."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 16),
            Opacity = 0.82,
        });

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        continueButton = new Button
        {
            Content = Read(localized, "Continue", "Continue"),
            IsEnabled = false,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 100,
        };
        continueButton.Click += (_, _) =>
        {
            SelectedMode = update.IsChecked == true ? CatalogueImportMode.Update : CatalogueImportMode.AddOnly;
            DialogResult = true;
        };
        var cancel = new Button
        {
            Content = Read(localized, "Cancel", "Cancel"),
            Padding = new Thickness(14, 5, 14, 5),
            MinWidth = 92,
        };
        cancel.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(continueButton);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        update.Checked += OnModeChanged;
        addOnly.Checked += OnModeChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    private void OnModeChanged(object sender, RoutedEventArgs e) => continueButton.IsEnabled = update.IsChecked == true || addOnly.IsChecked == true;

    private static string Read(IReadOnlyDictionary<string, string> localized, string key, string fallback) =>
        localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}

/// <summary>Read-only preview dialog. It commits only the immutable preview captured by the workflow VM.</summary>
internal sealed class CatalogueImportPreviewDialog : Window
{
    private readonly CatalogueWorkbookWorkflowViewModel workflow;
    private readonly CatalogueImportResult preview;
    private readonly IReadOnlyDictionary<string, string> localized;
    private readonly Button confirm;
    private readonly Button cancel;
    private readonly TextBlock status;
    private DataGrid issuesGrid = null!;
    private DataGrid affectedRowsGrid = null!;
    private bool closeAllowed;

    public CatalogueImportPreviewDialog(
        Window owner,
        CatalogueWorkbookWorkflowViewModel workflow,
        CatalogueImportResult preview,
        IReadOnlyDictionary<string, string> localized)
    {
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 940;
        Height = 700;
        MinWidth = 640;
        MinHeight = 480;
        Title = Read(localized, "CatalogueImportPreviewTitle", "Catalogue import preview");
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.preview = preview ?? throw new ArgumentNullException(nameof(preview));
        this.localized = localized ?? throw new ArgumentNullException(nameof(localized));
        Closing += OnClosing;
        workflow.PropertyChanged += OnWorkflowPropertyChanged;

        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        confirm = new Button { Content = Read(localized, "ConfirmImport", "Confirm import"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), MinWidth = 130 };
        confirm.Click += ConfirmAsync;
        cancel = new Button { Content = Read(localized, "CatalogueImportCancel", "Cancel"), Padding = new Thickness(14, 5, 14, 5), MinWidth = 92 };
        cancel.Click += (_, _) => { closeAllowed = true; DialogResult = false; };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        var content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(status);
        panel.Children.Add(BuildContext());
        panel.Children.Add(BuildSummary());
        panel.Children.Add(BuildNotices());
        panel.Children.Add(BuildIssues());
        panel.Children.Add(BuildAffectedRows());
        panel.Children.Add(BuildCategories());
        content.Content = panel;
        root.Children.Add(content);
        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateState();
    }

    private Grid BuildContext()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddContextRow(grid, 0, Read(localized, "CatalogueImportSource", "Source file"), preview.Preview.SourceName ?? string.Empty);
        AddContextRow(grid, 1, Read(localized, "CatalogueImportMode", "Mode"), preview.Preview.Mode == CatalogueImportMode.Update ? Read(localized, "CatalogueImportUpdateMode", "Update") : Read(localized, "CatalogueImportAddOnlyMode", "Add-only"));
        AddContextRow(grid, 2, Read(localized, "CatalogueImportAuthority", "Authority"), workflow.IsAuthoritative ? Read(localized, "Authoritative", "Authoritative") : Read(localized, "CatalogueImportReadOnly", "Read-only: confirmation is unavailable."));
        return grid;
    }

    private WrapPanel BuildSummary()
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        AddBadge(panel, "Products", preview.Preview.ProductCreateCount, "Create");
        AddBadge(panel, "Products", preview.Preview.ProductModifyCount, "Modify");
        AddBadge(panel, "Products", preview.Preview.ProductActivateCount, "Activate");
        AddBadge(panel, "Products", preview.Preview.ProductDeactivateCount, "Deactivate");
        AddBadge(panel, "OptionGroups", preview.Preview.OptionGroupCreateCount, "Create");
        AddBadge(panel, "OptionGroups", preview.Preview.OptionGroupModifyCount, "Modify");
        AddBadge(panel, "Options", preview.Preview.OptionCreateCount, "Create");
        AddBadge(panel, "Options", preview.Preview.OptionModifyCount, "Modify");
        AddBadge(panel, "Options", preview.Preview.OptionActivateCount, "Activate");
        AddBadge(panel, "Options", preview.Preview.OptionDeactivateCount, "Deactivate");
        AddBadge(panel, "CatalogueImportNewCategories", preview.Preview.NewCategoryCount, "New Categories");
        AddBadge(panel, "Errors", preview.Preview.ErrorCount, "Errors");
        AddBadge(panel, "Warnings", preview.Preview.WarningCount, "Warnings");
        return panel;
    }

    private StackPanel BuildNotices()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = Read(localized, "NoDatabaseChangeYet", "No database change has occurred yet."), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = Read(localized, "OmittedRowsNotDeleted", "Rows omitted from the workbook are not deleted."), TextWrapping = TextWrapping.Wrap });
        if (preview.Preview.Mode == CatalogueImportMode.AddOnly)
            panel.Children.Add(new TextBlock { Text = Read(localized, "AddOnlyExistingNotice", "Existing Product/OptionGroup/Option records are not updated in Add-only mode."), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private StackPanel BuildIssues()
    {
        var heading = new TextBlock { Text = Read(localized, "ErrorsAndWarnings", "Errors and warnings"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) };
        issuesGrid = new DataGrid { ItemsSource = workflow.Issues, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 190, MinHeight = 90, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = double.NaN };
        AddColumn(issuesGrid, Read(localized, "Severity", "Severity"), "SeverityText", 95);
        AddColumn(issuesGrid, Read(localized, "Worksheet", "Worksheet"), "Worksheet", 120);
        AddColumn(issuesGrid, Read(localized, "Row", "Row"), "Row", 58);
        AddColumn(issuesGrid, Read(localized, "Field", "Field"), "Field", 120);
        AddColumn(issuesGrid, Read(localized, "Message", "Message"), "Message", 480, wrap: true);
        var panel = new StackPanel();
        panel.Children.Add(heading);
        panel.Children.Add(issuesGrid);
        return panel;
    }

    private StackPanel BuildAffectedRows()
    {
        var heading = new TextBlock { Text = Read(localized, "AffectedRows", "Affected rows"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        affectedRowsGrid = new DataGrid { ItemsSource = workflow.AffectedRows, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 130, MinHeight = 70 };
        AddColumn(affectedRowsGrid, Read(localized, "Worksheet", "Worksheet"), "Worksheet", 120);
        AddColumn(affectedRowsGrid, Read(localized, "Row", "Row"), "ExcelRow", 70);
        AddColumn(affectedRowsGrid, Read(localized, "Entity", "Entity"), "EntityType", 120);
        AddColumn(affectedRowsGrid, Read(localized, "Actions", "Actions"), "Actions", 300, wrap: true);
        var panel = new StackPanel();
        panel.Children.Add(heading);
        panel.Children.Add(affectedRowsGrid);
        return panel;
    }

    private StackPanel BuildCategories()
    {
        var heading = new TextBlock { Text = Read(localized, "NewCategories", "New Categories"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        var list = new ListBox { MinHeight = 28, MaxHeight = 100, ItemsSource = preview.Plan?.NewCategories.Select(category => $"{category.Name}{(string.IsNullOrWhiteSpace(category.ShortCode) ? string.Empty : $" ({category.ShortCode})")}").ToArray() ?? [] };
        var panel = new StackPanel();
        panel.Children.Add(heading);
        panel.Children.Add(list);
        return panel;
    }

    private async void ConfirmAsync(object sender, RoutedEventArgs e)
    {
        if (!workflow.CanConfirm) return;
        SetActionState(false);
        status.Text = Read(localized, "CatalogueImportCommitting", "Saving Catalogue changes…");
        var result = await workflow.CommitPreviewAsync();
        status.Text = workflow.Outcome switch
        {
            CatalogueImportWorkflowOutcome.CommitFailed => string.Join(Environment.NewLine, workflow.Issues.Select(issue => issue.Message)),
            CatalogueImportWorkflowOutcome.NoChange => Read(localized, "CatalogueImportNoChange", "No Catalogue change was needed."),
            CatalogueImportWorkflowOutcome.RefreshFailed => Read(localized, "CatalogueImportRefreshFailed", "Import succeeded, but the interface refresh failed; restart or refresh before continuing."),
            CatalogueImportWorkflowOutcome.Changed => Read(localized, "CatalogueImportSucceeded", "Catalogue import succeeded."),
            _ => result.Succeeded ? Read(localized, "CatalogueImportSucceeded", "Catalogue import succeeded.") : Read(localized, "CatalogueImportFailed", "Catalogue import failed.")
        };
        closeAllowed = true;
        SetActionState(workflow.CanConfirm);
    }

    private void SetActionState(bool enabled)
    {
        confirm.IsEnabled = enabled && workflow.CanConfirm && !workflow.IsCompleted;
        cancel.IsEnabled = !workflow.IsBusy;
        cancel.Content = workflow.IsCompleted
            ? Read(localized, "Close", "Close")
            : Read(localized, "CatalogueImportCancel", "Cancel");
    }

    private void UpdateState()
    {
        SetActionState(workflow.CanConfirm);
        issuesGrid.ItemsSource = workflow.Issues;
        affectedRowsGrid.ItemsSource = workflow.AffectedRows;
        if (!workflow.IsAuthoritative)
            status.Text = Read(localized, "CatalogueImportReadOnly", "Read-only: confirmation is unavailable.");
    }

    private void OnWorkflowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) UpdateState();
        else Dispatcher.BeginInvoke(UpdateState);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!closeAllowed && workflow.IsBusy)
        {
            e.Cancel = true;
            return;
        }
        if (!closeAllowed && workflow.LastCommit is null)
            closeAllowed = true; // close/cancel before Confirm is always transient-only
        workflow.PropertyChanged -= OnWorkflowPropertyChanged;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (workflow.IsBusy)
        {
            e.Handled = true;
            return;
        }

        closeAllowed = true;
        DialogResult = false;
        e.Handled = true;
    }

    private static void AddContextRow(Grid grid, int row, string label, string value)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new TextBlock { Text = label + ":", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 3) };
        var valueBlock = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) };
        Grid.SetRow(labelBlock, row); Grid.SetColumn(labelBlock, 0);
        Grid.SetRow(valueBlock, row); Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock); grid.Children.Add(valueBlock);
    }

    private void AddBadge(WrapPanel panel, string labelKey, int count, string actionKey)
    {
        var label = Read(localized, labelKey, labelKey);
        var action = Read(localized, actionKey, actionKey);
        panel.Children.Add(new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 6, 5),
            Child = new TextBlock { Text = $"{label} — {action}: {count}" },
        });
    }

    private static void AddColumn(DataGrid grid, string headerKey, string bindingPath, double width, bool wrap = false)
    {
        var column = new DataGridTextColumn { Header = headerKey, Binding = new Binding(bindingPath), Width = new DataGridLength(width) };
        if (wrap)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(2)));
            column.ElementStyle = style;
        }
        grid.Columns.Add(column);
    }

    private static string Read(IReadOnlyDictionary<string, string> localized, string key, string fallback) =>
        localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
