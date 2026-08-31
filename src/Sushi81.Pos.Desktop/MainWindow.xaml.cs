using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;
using DomainSelectionMode = Sushi81.Pos.Domain.SelectionMode;

namespace Sushi81.Pos.Desktop;

public partial class MainWindow : Window
{
    private bool loaded;
    private readonly CatalogueHeaderSet catalogueHeaders = new();

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (viewModel.Admin is { } admin) admin.FilterRefreshFailed += OnFilterRefreshFailed;
        ApplyCatalogueHeaders();
        Closed += (_, _) => (DataContext as ShellViewModel)?.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded || DataContext is not ShellViewModel viewModel || viewModel.Admin is null && viewModel.Entry is null) return;
        loaded = true;
        try
        {
            if (viewModel.Admin is { } admin) { await admin.RefreshAsync(); await admin.LoadSettingsAsync(); }
            if (viewModel.Entry is { } entry) await entry.RefreshAsync();
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
        ApplyCatalogueHeaders();
    }

    private async void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel || e.AddedItems.OfType<LanguageOption>().SingleOrDefault() is not { } language) return;
        try { await viewModel.ChangeLanguageAsync(language); ApplyCatalogueHeaders(); }
        catch { MessageBox.Show(this, viewModel.LanguageSaveFailure, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ApplyCatalogueHeaders()
    {
        if (DataContext is not ShellViewModel viewModel || catalogueGrid.Columns.Count < 6) return;
        catalogueHeaders.Apply(viewModel.Localized);
        var values = catalogueHeaders.Values;
        for (var index = 0; index < values.Count; index++) catalogueGrid.Columns[index].Header = values[index];
    }

    private async void OnRefreshCatalogue(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Admin: { } admin }) await admin.RefreshAsync();
    }

    private async void OnAddOrderProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Entry: { } entry }) return;
        try
        {
            await entry.AddSelectedProductAsync();
            if (entry.PendingProduct is { } product)
            {
                var dialog = new OptionSelectionDialog(this, product, null);
                if (dialog.ShowDialog() == true) entry.AddConfiguredLine(product, dialog.SelectedOptionIds, dialog.CustomAdjustments, dialog.Quantity);
            }
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnEditOrderLine(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ShellViewModel { Entry: { } entry } || orderCartList.SelectedItem is not OrderEntryCartLineViewModel line) return;
        var productId = line.Draft.Product.Product.Id;
        _ = EditOrderLineAsync(entry, line, productId);
        e.Handled = true;
    }

    private async Task EditOrderLineAsync(OrderEntryShellViewModel entry, OrderEntryCartLineViewModel line, Guid productId)
    {
        try
        {
            var product = await entry.GetActiveProductForEditAsync(productId);
            if (product is null) { MessageBox.Show(this, LocalizedText(this, "ProductInactive", "Le produit n’est plus actif."), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var dialog = new OptionSelectionDialog(this, product, line);
            if (dialog.ShowDialog() == true) entry.UpdateConfiguredLine(line, dialog.SelectedOptionIds, dialog.CustomAdjustments, dialog.Quantity);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnDecreaseOrderQuantity(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry } && (sender as Button)?.Tag is OrderEntryCartLineViewModel line)
            if (line.Quantity > 1) entry.ChangeQuantity(line, line.Quantity - 1);
    }

    private void OnIncreaseOrderQuantity(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry } && (sender as Button)?.Tag is OrderEntryCartLineViewModel line)
            entry.ChangeQuantity(line, line.Quantity + 1);
    }

    private void OnRemoveOrderLine(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry } && (sender as Button)?.Tag is OrderEntryCartLineViewModel line) entry.RemoveLine(line);
    }

    private void OnManualOrderTotalChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry } && sender is TextBox box) entry.SetManualTotal(box.Text);
    }

    private async void OnConfirmOrder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Entry: { } entry }) return;
        try { await entry.ConfirmAsync(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnNewOrder(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry }) entry.StartNewOrder();
    }

    private async void OnReloadOrder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Entry: { } entry }) return;
        if (!await entry.ReloadOrderAsync())
            MessageBox.Show(this, entry.ValidationMessage, LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnFilterRefreshFailed(object? sender, EventArgs e)
    {
        MessageBox.Show(this, LocalizedText(this, "ValidationGeneric", "The operation failed."),
            LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void OnNewProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin }) return;
        var categories = admin.Categories.ToArray();
        if (categories.Length == 0)
        {
            MessageBox.Show(this, LocalizedText(this, "CreateCategoryFirst", "Create a category first."), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new ProductEditorDialog(this, admin, null, categories);
        if (dialog.ShowDialog() == true) await admin.RefreshAsync();
    }

    private async void OnEditProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin } || admin.SelectedProduct is not { } selected) return;
        var draft = await admin.LoadProductAsync(selected.Id);
        if (draft is null) { await admin.RefreshAsync(); return; }
        var dialog = new ProductEditorDialog(this, admin, draft, admin.Categories.ToArray());
        if (dialog.ShowDialog() == true) await admin.RefreshAsync();
    }

    private async void OnToggleProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin } || admin.SelectedProduct is not { } product) return;
        var result = await admin.SetProductActiveAsync(product.Id, !product.IsActive);
        if (!result.Succeeded) ShowResultError(result); else await admin.RefreshAsync();
    }

    private async void OnBulkActivate(object sender, RoutedEventArgs e) => await RunBulkActiveStateAsync(targetIsActive: true);

    private async void OnBulkDeactivate(object sender, RoutedEventArgs e) => await RunBulkActiveStateAsync(targetIsActive: false);

    private async Task RunBulkActiveStateAsync(bool targetIsActive)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin }) return;
        M03Presentation.BulkWorkflowResult workflow;
        try
        {
            workflow = await admin.ExecuteBulkActiveStateWorkflowAsync(
                targetIsActive,
                confirmation => MessageBox.Show(
                    this,
                    confirmation.Format(((ShellViewModel)DataContext).Localized),
                    LocalizedText(this, "ShellTitle", "Sushi81 POS"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            ShowResultError(OperationResult.Failure(new ValidationIssue("products", "The catalogue could not be refreshed.")));
            return;
        }

        if (workflow.Outcome == M03Presentation.BulkWorkflowOutcome.NoOp)
        {
            MessageBox.Show(this, LocalizedText(this, "BulkNoChange", "No change is needed for the filtered products."),
                LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (workflow.Outcome == M03Presentation.BulkWorkflowOutcome.Cancelled)
            return;

        var result = workflow.Mutation!;
        if (!result.Succeeded)
        {
            ShowResultError(result);
            return;
        }

        var changed = result.Value?.ChangedCount ?? workflow.Confirmation.ChangedCount;
        MessageBox.Show(this, string.Format(CultureInfo.CurrentCulture, LocalizedText(this, "BulkSuccess", "{0} product(s) updated."), changed),
            LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnDeleteProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin } || admin.SelectedProduct is not { } product) return;
        if (MessageBox.Show(this, $"{LocalizedText(this, "DeleteConfirm", "Delete this product permanently?")}\n\n{product.Code} — {product.Name}", LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = await admin.DeleteProductAsync(product.Id);
        if (!result.Succeeded) ShowResultError(result); else await admin.RefreshAsync();
    }

    private async void OnManageCategories(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin }) return;
        var dialog = new CategoryManagerDialog(this, admin);
        if (dialog.ShowDialog() == true) await admin.RefreshAsync();
    }

    private async void OnSettingsSelected(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Admin: { } admin })
        {
            try { await admin.LoadSettingsAsync(); admin.SetSettingsValidationMessage(string.Empty); } catch { }
        }
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin }) return;
        var result = await admin.SaveSettingsAsync();
        if (!result.Succeeded)
        {
            admin.SetSettingsValidationMessage(M03Presentation.FormatIssues(result, admin is not null ? ((ShellViewModel)DataContext).Localized : new Dictionary<string, string>()));
            ShowResultError(result);
        }
        else { admin.SetSettingsValidationMessage(string.Empty); MessageBox.Show(this, LocalizedText(this, "Saved", "Saved."), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void OnReloadSettings(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Admin: { } admin }) { await admin.LoadSettingsAsync(); admin.SetSettingsValidationMessage(string.Empty); }
    }

    private void ShowResultError(OperationResult result)
    {
        var localized = (DataContext as ShellViewModel)?.Localized ?? new Dictionary<string, string>();
        MessageBox.Show(this, result.Issues.Count == 0 ? LocalizedText(this, "ValidationGeneric", "The operation failed.") : M03Presentation.FormatIssues(result, localized), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed class OptionSelectionDialog : Window
    {
        private readonly OrderEntryProduct product;
        private readonly Dictionary<Guid, List<FrameworkElement>> controls = [];
        private readonly StackPanel customPanel = new();
        private readonly List<(TextBox Label, TextBox Amount, Button Remove)> customRows = [];
        private readonly TextBox quantity;
        private readonly IReadOnlyDictionary<string, string> localized;
        public OptionSelectionDialog(Window owner, OrderEntryProduct product, OrderEntryCartLineViewModel? existing)
        {
            this.product = product;
            localized = (owner.DataContext as ShellViewModel)?.Localized ?? new Dictionary<string, string>();
            Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = Label("Options", "Options"); Width = 520; Height = 620; MinHeight = 420;
            var root = new DockPanel { Margin = new Thickness(14) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = Label("Cancel", "Annuler"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => DialogResult = false;
            var ok = new Button { Content = Label("Add", "Ajouter"), Padding = new Thickness(12, 5, 12, 5) }; ok.Click += (_, _) => Accept(); buttons.Children.Add(cancel); buttons.Children.Add(ok); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = $"{product.Aggregate.Product.Code} — {product.Aggregate.Product.Name}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            quantity = AddText(panel, Label("Quantity", "Quantité"), existing?.Quantity.ToString(CultureInfo.InvariantCulture) ?? "1");
            if (product.Aggregate.Product.OptionsEnabled)
                foreach (var group in product.Aggregate.Groups.OrderBy(group => group.DisplayOrder)) AddGroup(panel, group, existing?.Draft.SelectedOptionIds ?? []);
            panel.Children.Add(new TextBlock { Text = Label("CustomAdjustments", "Ajustements personnalisés (par unité)"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var current in existing?.Draft.CustomAdjustments ?? []) AddCustomRow(current);
            var addCustom = new Button { Content = "+ " + Label("AddAdjustment", "Ajustement"), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) }; addCustom.Click += (_, _) => AddCustomRow(null);
            panel.Children.Add(customPanel); panel.Children.Add(addCustom);
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        }

        public IReadOnlyList<Guid> SelectedOptionIds { get; private set; } = [];
        public IReadOnlyList<OrderLineAdjustmentDraft> CustomAdjustments { get; private set; } = [];
        public int Quantity { get; private set; }

        private void AddCustomRow(OrderLineAdjustmentDraft? current)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBox { Width = 210, ToolTip = Label("AdjustmentLabel", "Libellé"), Text = current?.Label ?? string.Empty };
            var amount = new TextBox { Width = 90, Margin = new Thickness(6, 0, 0, 0), ToolTip = Label("AdjustmentAmount", "Montant TTC"), Text = current is null ? string.Empty : current.AmountTtcPerUnit.Euros.ToString("0.00", CultureInfo.InvariantCulture) };
            var remove = new Button { Content = "×", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(5, 2, 5, 2) };
            var tuple = (label, amount, remove); remove.Click += (_, _) => { customPanel.Children.Remove(row); customRows.Remove(tuple); }; row.Children.Add(label); row.Children.Add(amount); row.Children.Add(remove); customPanel.Children.Add(row); customRows.Add(tuple);
        }

        private void AddGroup(Panel panel, OptionGroup group, IReadOnlyList<Guid> existing)
        {
            var title = $"{group.Name} — {(group.SelectionMode == DomainSelectionMode.Single ? Label("Single", "SINGLE") : $"{Label("Multi", "MULTI")} {group.MinSelections}-{group.MaxSelections}")} {(group.IsRequired ? Label("Required", "requis") : Label("Optional", "optionnel"))}";
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            var list = new List<FrameworkElement>(); controls[group.Id] = list;
            foreach (var option in (product.Aggregate.OptionsByGroup.TryGetValue(group.Id, out var values) ? values : []).Where(option => option.IsActive).OrderBy(option => option.DisplayOrder))
            {
                FrameworkElement control = group.SelectionMode == DomainSelectionMode.Single
                    ? new RadioButton { Content = $"{option.Name} ({option.PriceAdjustmentTtc.Euros:0.00} €)", GroupName = $"group-{group.Id}", IsChecked = existing.Contains(option.Id), Tag = option.Id }
                    : new CheckBox { Content = $"{option.Name} ({option.PriceAdjustmentTtc.Euros:0.00} €)", IsChecked = existing.Contains(option.Id), Tag = option.Id };
                list.Add(control); panel.Children.Add(control);
            }
        }

        private static TextBox AddText(Panel panel, string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) }; row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center }); var box = new TextBox { Width = 100, Text = value }; row.Children.Add(box); panel.Children.Add(row); return box;
        }

        private void Accept()
        {
            if (!int.TryParse(quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuantity) || parsedQuantity <= 0) { MessageBox.Show(this, Label("InvalidQuantity", "La quantité doit être un entier positif.")); return; }
            var selected = new List<Guid>();
            if (product.Aggregate.Product.OptionsEnabled)
            {
                foreach (var group in product.Aggregate.Groups)
                {
                    var values = controls[group.Id].Where(control => control is RadioButton radio && radio.IsChecked == true || control is CheckBox check && check.IsChecked == true).Select(control => (Guid)control.Tag!).ToArray();
                    var min = group.SelectionMode == DomainSelectionMode.Single ? (group.IsRequired ? 1 : 0) : group.MinSelections ?? 0;
                    var max = group.SelectionMode == DomainSelectionMode.Single ? 1 : group.MaxSelections ?? 0;
                    if (values.Length < min || values.Length > max) { MessageBox.Show(this, string.Format(CultureInfo.CurrentCulture, Label("InvalidOptions", "La sélection du groupe « {0} » est invalide."), group.Name)); return; }
                    selected.AddRange(values);
                }
            }
            var custom = new List<OrderLineAdjustmentDraft>();
            foreach (var row in customRows)
            {
                var label = row.Label.Text.Trim();
                if (label.Length == 0 && string.IsNullOrWhiteSpace(row.Amount.Text)) continue;
                if (label.Length == 0 || !decimal.TryParse(row.Amount.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) { MessageBox.Show(this, Label("InvalidAdjustment", "Chaque ajustement doit avoir un libellé et un montant valides.")); return; }
                custom.Add(new(null, null, label, Money.FromEuros(amount), OrderAdjustmentKind.CustomAdjustment, custom.Count));
            }
            SelectedOptionIds = selected; CustomAdjustments = custom; Quantity = parsedQuantity; DialogResult = true;
        }

        private string Label(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
    }

    private static string LocalizedText(Window owner, string key, string fallback) => owner.DataContext is ShellViewModel viewModel && viewModel.Localized.TryGetValue(key, out var value) ? value : fallback;

    private sealed class CategoryManagerDialog : Window
    {
        private readonly M03ShellViewModel admin;
        private readonly ListBox list = null!;
        private readonly TextBox name = null!;
        private readonly TextBlock validation = null!;
        private readonly Button create = null!;
        private readonly Button rename = null!;
        private readonly Button save = null!;
        private readonly Button cancel = null!;
        private readonly Button close = null!;
        private readonly CategoryEditBuffer edit = new();
        private bool closeAllowed;

        public CategoryManagerDialog(Window owner, M03ShellViewModel admin)
        {
            this.admin = admin; Owner = owner; Width = 440; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, "ManageCategories", "Categories");
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            list = new ListBox { ItemsSource = admin.Categories, DisplayMemberPath = "Name", MinHeight = 120, VerticalAlignment = VerticalAlignment.Stretch };
            list.SelectionChanged += (_, _) => { if (!edit.IsEditing && list.SelectedItem is CategorySummary category) name.Text = category.Name; UpdateButtons(); };
            Grid.SetRow(list, 0); root.Children.Add(list);
            var heading = new TextBlock { Text = LocalizedText(owner, "CategoryEdit", "Category edit"), Margin = new Thickness(0, 10, 0, 2) }; Grid.SetRow(heading, 1); root.Children.Add(heading);
            name = new TextBox { Margin = new Thickness(0, 0, 0, 4), IsEnabled = false }; name.TextChanged += (_, _) => validation.Text = string.Empty; Grid.SetRow(name, 2); root.Children.Add(name);
            validation = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; Grid.SetRow(validation, 3); root.Children.Add(validation);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
            create = new Button { Content = LocalizedText(owner, "CreateCategory", "New"), MinWidth = 84, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top }; create.Click += (_, _) => { edit.BeginCreate(); name.IsEnabled = true; name.Clear(); validation.Text = string.Empty; UpdateButtons(); FocusName(selectAll: false); };
            rename = new Button { Content = LocalizedText(owner, "RenameCategory", "Rename"), MinWidth = 96, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top, IsEnabled = false }; rename.Click += (_, _) => { if (list.SelectedItem is CategorySummary category) { edit.BeginRename(category.Id, category.Name); name.IsEnabled = true; name.Text = category.Name; validation.Text = string.Empty; UpdateButtons(); FocusName(selectAll: true); } };
            save = new Button { Content = LocalizedText(owner, "Save", "Save"), MinWidth = 112, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top, IsEnabled = false }; save.Click += Save;
            cancel = new Button { Content = LocalizedText(owner, "CategoryEditCancel", "Cancel"), MinWidth = 172, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top, IsEnabled = false }; cancel.Click += (_, _) => { edit.Cancel(); name.Clear(); validation.Text = string.Empty; UpdateButtons(); };
            close = new Button { Content = LocalizedText(owner, "Close", "Close"), MinWidth = 84, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top }; close.Click += (_, _) => { closeAllowed = true; DialogResult = true; Close(); };
            buttons.Children.Add(create); buttons.Children.Add(rename); buttons.Children.Add(save); buttons.Children.Add(cancel); buttons.Children.Add(close); Grid.SetRow(buttons, 4); root.Children.Add(buttons); Content = root;
            UpdateButtons();
            Closing += (_, _) => { if (!closeAllowed && edit.IsEditing) edit.Cancel(); };
        }

        private void UpdateButtons()
        {
            var editing = edit.IsEditing;
            create.IsEnabled = edit.CanBeginEdit;
            rename.IsEnabled = edit.CanBeginEdit && list.SelectedItem is CategorySummary;
            name.IsEnabled = editing;
            save.IsEnabled = edit.CanSave;
            cancel.IsEnabled = edit.CanCancel;
        }

        private void FocusName(bool selectAll)
        {
            name.Focus();
            Keyboard.Focus(name);
            if (selectAll) name.SelectAll(); else name.CaretIndex = name.Text.Length;
        }

        private async void Save(object sender, RoutedEventArgs e)
        {
            edit.SetName(name.Text);
            var result = edit.CategoryId is { } id ? await admin.RenameCategoryAsync(id, edit.Name) : await admin.CreateCategoryAsync(edit.Name);
            if (!result.Succeeded) { validation.Text = M03Presentation.FormatIssues(result, ((ShellViewModel)Owner.DataContext).Localized); return; }
            await admin.RefreshAsync(); edit.CompleteSave(); name.Clear(); validation.Text = string.Empty; UpdateButtons(); list.Focus();
        }
    }

    private sealed class ProductEditorDialog : Window
    {
        private readonly M03ShellViewModel admin; private readonly ProductDraft? existing; private readonly IReadOnlyDictionary<string, string> localized; private readonly ComboBox category; private readonly TextBox code; private readonly TextBox productName; private readonly TextBox price; private readonly TextBox vat; private readonly CheckBox active; private readonly CheckBox discount; private readonly CheckBox options; private readonly StackPanel groupsPanel; private readonly TextBlock validation; private readonly List<GroupEditor> groups = [];
        private bool dirty; private bool closeAllowed;

        public ProductEditorDialog(Window owner, M03ShellViewModel admin, ProductDraft? existing, IReadOnlyList<CategorySummary> categories)
        {
            this.admin = admin; this.existing = existing; localized = (owner.DataContext as ShellViewModel)?.Localized ?? new Dictionary<string, string>(StringComparer.Ordinal); Owner = owner; Width = 560; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, existing is null ? "NewProduct" : "Edit", existing is null ? "New product" : "Edit");
            var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Margin = new Thickness(16) } }; var panel = (StackPanel)root.Content;
            validation = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(validation);
            code = AddText(panel, LocalizedText(owner, "Code", "Code"), existing?.Code ?? string.Empty); productName = AddText(panel, LocalizedText(owner, "Name", "Name"), existing?.Name ?? string.Empty);
            panel.Children.Add(new TextBlock { Text = LocalizedText(owner, "Category", "Category"), Margin = new Thickness(0, 8, 0, 2) }); category = new ComboBox { ItemsSource = categories, DisplayMemberPath = "Name", SelectedValuePath = "Id" }; category.SelectedValue = existing?.CategoryId ?? (categories.Count > 0 ? categories[0].Id : Guid.Empty); panel.Children.Add(category);
            price = AddText(panel, LocalizedText(owner, "PriceTtc", "TTC price"), existing?.PriceTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00"); vat = AddText(panel, LocalizedText(owner, "Vat", "VAT %"), existing?.VatRate.ToString(CultureInfo.InvariantCulture) ?? "10");
            active = AddCheck(panel, LocalizedText(owner, "Active", "Active"), existing?.IsActive ?? true); discount = AddCheck(panel, LocalizedText(owner, "DiscountEligible", "Retrait discount eligible"), existing?.DiscountEligible ?? true); options = AddCheck(panel, LocalizedText(owner, "OptionsEnabled", "Options enabled"), existing?.OptionsEnabled ?? false);
            panel.Children.Add(new TextBlock { Text = LocalizedText(owner, "OptionGroups", "Option groups"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            var addGroup = new Button { Content = "+ " + LocalizedText(owner, "OptionGroups", "Group"), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left }; addGroup.Click += (_, _) => { dirty = true; AddGroup(null); }; panel.Children.Add(addGroup);
            groupsPanel = new StackPanel(); panel.Children.Add(groupsPanel);
            foreach (var group in existing?.Groups ?? []) AddGroup(group);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) }; var save = new Button { Content = LocalizedText(owner, "Save", "Save"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) }; save.Click += Save; var cancel = new Button { Content = LocalizedText(owner, "Cancel", "Cancel"), Padding = new Thickness(14, 5, 14, 5) }; cancel.Click += (_, _) => { closeAllowed = true; DialogResult = false; Close(); }; buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = root;
            foreach (var box in new[] { code, productName, price, vat }) box.TextChanged += (_, _) => dirty = true;
            category.SelectionChanged += (_, _) => dirty = true; foreach (var box in new[] { active, discount, options }) { box.Checked += (_, _) => dirty = true; box.Unchecked += (_, _) => dirty = true; }
            Closing += OnClosing;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (closeAllowed || !dirty) return;
            e.Cancel = true;
            validation.Text = Label("DirtyEditorClose", "Save or cancel your changes before closing.");
            MessageBox.Show(this, validation.Text, Label("ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static TextBox AddText(Panel panel, string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2) }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
        private static CheckBox AddCheck(Panel panel, string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(box); return box; }

        private async void Save(object sender, RoutedEventArgs e)
        {
            if (!TryBuildDraft(out var draft, out var issues)) { ShowIssues(issues); return; }
            var result = existing is null ? await admin.CreateProductAsync(draft) : await admin.UpdateProductAsync(existing.Id, draft);
            if (!result.Succeeded) { ShowIssues(result.Issues); return; }
            closeAllowed = true; DialogResult = true; Close();
        }

        private bool TryBuildDraft(out ProductDraft draft, out IReadOnlyList<ValidationIssue> issues)
        {
            var errors = new List<ValidationIssue>(); draft = null!; var categoryId = category.SelectedValue is Guid selectedCategory ? selectedCategory : Guid.Empty;
            if (categoryId == Guid.Empty) errors.Add(new("category", "A category is required.", ValidationCodes.CategoryMissing));
            var priceValue = Money.Zero; var vatValue = 0m;
            if (!M03Presentation.TryParseMoney(price.Text, "price", out priceValue, out var priceIssue) && priceIssue is not null) errors.Add(priceIssue);
            if (!M03Presentation.TryParseDecimal(vat.Text, "vat", out vatValue, out var vatIssue) && vatIssue is not null) errors.Add(vatIssue);
            var parsedGroups = new List<OptionGroupDraft>(); foreach (var group in groups) if (group.TryToDraft(parsedGroups.Count, out var value, out var groupIssues)) parsedGroups.Add(value!); else errors.AddRange(groupIssues);
            if (errors.Count == 0) draft = new ProductDraft(existing?.Id ?? Guid.Empty, code.Text, productName.Text, categoryId, priceValue, vatValue, active.IsChecked == true, discount.IsChecked == true, options.IsChecked == true, parsedGroups);
            issues = errors; return errors.Count == 0;
        }

        private void ShowIssues(IEnumerable<ValidationIssue> issues)
        {
            var result = OperationResult.Failure(issues.ToArray()); var localized = (Owner.DataContext as ShellViewModel)?.Localized ?? new Dictionary<string, string>(); validation.Text = M03Presentation.FormatIssues(result, localized);
        }

        private void AddGroup(OptionGroupDraft? draft) { var editor = new GroupEditor(this, draft); groups.Add(editor); groupsPanel.Children.Add(editor.Container); }
        private void RemoveGroup(GroupEditor editor) { dirty = true; groups.Remove(editor); groupsPanel.Children.Remove(editor.Container); }
        private void MoveGroup(GroupEditor editor, int delta) { var index = groups.IndexOf(editor); var target = index + delta; if (index < 0 || target < 0 || target >= groups.Count) return; dirty = true; (groups[index], groups[target]) = (groups[target], groups[index]); groupsPanel.Children.RemoveAt(index); groupsPanel.Children.Insert(target, editor.Container); }

        private sealed class GroupEditor
        {
            private readonly ProductEditorDialog owner; private readonly Guid id; private readonly TextBox name; private readonly ComboBox mode; private readonly CheckBox required; private readonly TextBox min; private readonly TextBox max; private readonly StackPanel optionsPanel; private readonly List<OptionEditor> options = [];
            public GroupEditor(ProductEditorDialog owner, OptionGroupDraft? draft)
            {
                this.owner = owner; id = draft?.Id ?? Guid.Empty; Container = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0) }; Root = new StackPanel(); Container.Child = Root;
                name = AddText(Root, owner.Label("Name", "Name"), draft?.Name ?? string.Empty); name.TextChanged += (_, _) => owner.dirty = true;
                var modeRow = new Grid { Margin = new Thickness(0, 5, 0, 0) }; modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); var modeLabel = new TextBlock { Text = owner.Label("SelectionMode", "Mode"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(modeLabel, 0); modeRow.Children.Add(modeLabel); mode = new ComboBox { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { new ModeItem(DomainSelectionMode.Single, owner.Label("Single", "Single")), new ModeItem(DomainSelectionMode.Multi, owner.Label("Multi", "Multi")) }, DisplayMemberPath = "Label", SelectedValuePath = "Mode" }; mode.SelectedValue = draft?.SelectionMode ?? DomainSelectionMode.Single; mode.SelectionChanged += (_, _) => { owner.dirty = true; UpdateLimits(); }; Grid.SetColumn(mode, 2); modeRow.Children.Add(mode); Root.Children.Add(modeRow);
                required = new CheckBox { Content = owner.Label("Required", "Required"), IsChecked = draft?.IsRequired ?? false, Margin = new Thickness(0, 5, 0, 0) }; required.Checked += (_, _) => owner.dirty = true; required.Unchecked += (_, _) => owner.dirty = true; Root.Children.Add(required);
                var limits = new WrapPanel { Orientation = Orientation.Horizontal }; limits.Children.Add(new TextBlock { Text = owner.Label("Minimum", "Min"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 4, 3) }); min = new TextBox { Width = 55, Text = draft?.MinSelections?.ToString(CultureInfo.InvariantCulture) ?? "0", Margin = new Thickness(0, 3, 8, 3) }; limits.Children.Add(min); limits.Children.Add(new TextBlock { Text = owner.Label("Maximum", "Max"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 4, 3) }); max = new TextBox { Width = 55, Text = draft?.MaxSelections?.ToString(CultureInfo.InvariantCulture) ?? "1", Margin = new Thickness(0, 3, 0, 3) }; limits.Children.Add(max); Root.Children.Add(limits);
                var groupButtons = new WrapPanel { Orientation = Orientation.Horizontal }; var add = new Button { Content = "+ " + owner.Label("Options", "Option"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; add.Click += (_, _) => { owner.dirty = true; AddOption(null); }; var remove = new Button { Content = owner.Label("DeletePermanently", "Delete"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; remove.Click += (_, _) => owner.RemoveGroup(this); var up = new Button { Content = owner.Label("MoveUp", "Up"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; up.Click += (_, _) => owner.MoveGroup(this, -1); var down = new Button { Content = owner.Label("MoveDown", "Down"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; down.Click += (_, _) => owner.MoveGroup(this, 1); groupButtons.Children.Add(add); groupButtons.Children.Add(remove); groupButtons.Children.Add(up); groupButtons.Children.Add(down); Root.Children.Add(groupButtons);
                optionsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; Root.Children.Add(optionsPanel); foreach (var option in draft?.Options ?? []) AddOption(option); UpdateLimits(); min.TextChanged += (_, _) => owner.dirty = true; max.TextChanged += (_, _) => owner.dirty = true;
            }
            public StackPanel Root { get; }
            public Border Container { get; }
            public bool TryToDraft(int order, out OptionGroupDraft? value, out IReadOnlyList<ValidationIssue> issues)
            {
                var errors = new List<ValidationIssue>(); var selected = (DomainSelectionMode)(mode.SelectedValue ?? DomainSelectionMode.Single); int? minValue = null, maxValue = null;
                if (selected == DomainSelectionMode.Multi)
                {
                    if (!M03Presentation.TryParseInteger(min.Text, "groups", out var parsedMin, out var minIssue) && minIssue is not null) errors.Add(minIssue); else minValue = parsedMin;
                    if (!M03Presentation.TryParseInteger(max.Text, "groups", out var parsedMax, out var maxIssue) && maxIssue is not null) errors.Add(maxIssue); else maxValue = parsedMax;
                }
                var parsedOptions = new List<OptionDraft>(); foreach (var option in options) if (option.TryToDraft(parsedOptions.Count, out var parsed, out var optionIssue)) parsedOptions.Add(parsed!); else errors.Add(optionIssue!);
                value = errors.Count == 0 ? new OptionGroupDraft(id, name.Text, selected, required.IsChecked == true, minValue, maxValue, order, parsedOptions) : null; issues = errors; return errors.Count == 0;
            }
            private static TextBox AddText(Panel panel, string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2) }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
            private void AddOption(OptionDraft? draft) { OptionEditor? option = null; option = new OptionEditor(owner, draft, () => { if (option is not null) { owner.dirty = true; options.Remove(option); optionsPanel.Children.Remove(option.Root); } }, delta => { if (option is not null) MoveOption(option, delta); }); options.Add(option); optionsPanel.Children.Add(option.Root); }
            private void MoveOption(OptionEditor option, int delta) { var index = options.IndexOf(option); var target = index + delta; if (index < 0 || target < 0 || target >= options.Count) return; owner.dirty = true; (options[index], options[target]) = (options[target], options[index]); optionsPanel.Children.RemoveAt(index); optionsPanel.Children.Insert(target, option.Root); }
            private void UpdateLimits() { var multi = (DomainSelectionMode)(mode.SelectedValue ?? DomainSelectionMode.Single) == DomainSelectionMode.Multi; min.IsEnabled = multi; max.IsEnabled = multi; if (!multi) { min.Text = string.Empty; max.Text = string.Empty; } }
        }

        private sealed class OptionEditor
        {
            private readonly Guid id; private readonly TextBox name; private readonly TextBox adjustment; private readonly CheckBox active;
            public OptionEditor(ProductEditorDialog owner, OptionDraft? draft, Action remove, Action<int> move)
            {
                id = draft?.Id ?? Guid.Empty; Root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var namePanel = new StackPanel(); namePanel.Children.Add(new TextBlock { Text = owner.Label("OptionName", "Option name") }); name = new TextBox { Width = 130, Text = draft?.Name ?? string.Empty }; namePanel.Children.Add(name);
                var adjustmentPanel = new StackPanel { Margin = new Thickness(5, 0, 5, 0) }; adjustmentPanel.Children.Add(new TextBlock { Text = owner.Label("AdjustmentTtc", "TTC adjustment") }); adjustment = new TextBox { Width = 80, Text = draft?.PriceAdjustmentTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00" }; adjustmentPanel.Children.Add(adjustment);
                active = new CheckBox { Content = owner.Label("OptionActive", "Active option"), IsChecked = draft?.IsActive ?? true, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 5, 3) }; name.TextChanged += (_, _) => owner.dirty = true; adjustment.TextChanged += (_, _) => owner.dirty = true; active.Checked += (_, _) => owner.dirty = true; active.Unchecked += (_, _) => owner.dirty = true;
                var delete = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 15, 0, 0) }; delete.Click += (_, _) => remove(); var up = new Button { Content = owner.Label("MoveUp", "↑"), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 15, 0, 0) }; up.Click += (_, _) => move(-1); var down = new Button { Content = owner.Label("MoveDown", "↓"), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 15, 0, 0) }; down.Click += (_, _) => move(1); Root.Children.Add(namePanel); Root.Children.Add(adjustmentPanel); Root.Children.Add(active); Root.Children.Add(up); Root.Children.Add(down); Root.Children.Add(delete);
            }
            public StackPanel Root { get; }
            public bool TryToDraft(int order, out OptionDraft? value, out ValidationIssue? issue)
            {
                if (!M03Presentation.TryParseMoney(adjustment.Text, "options", out var amount, out issue)) { value = null; return false; }
                value = new OptionDraft(id, name.Text, amount, active.IsChecked == true, order); return true;
            }
        }

        private sealed record ModeItem(DomainSelectionMode Mode, string Label);
        private string Label(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
    }
}
