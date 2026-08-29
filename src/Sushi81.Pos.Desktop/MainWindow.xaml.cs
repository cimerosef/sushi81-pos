using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;
using DomainSelectionMode = Sushi81.Pos.Domain.SelectionMode;

namespace Sushi81.Pos.Desktop;

public partial class MainWindow : Window
{
    private bool loaded;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded || DataContext is not ShellViewModel { Admin: { } admin }) return;
        loaded = true;
        try { await admin.RefreshAsync(); await admin.LoadSettingsAsync(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel || e.AddedItems.OfType<LanguageOption>().SingleOrDefault() is not { } language) return;
        try { await viewModel.ChangeLanguageAsync(language); }
        catch { MessageBox.Show(this, viewModel.LanguageSaveFailure, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnRefreshCatalogue(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Admin: { } admin }) await admin.RefreshAsync();
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

    private static string LocalizedText(Window owner, string key, string fallback) => owner.DataContext is ShellViewModel viewModel && viewModel.Localized.TryGetValue(key, out var value) ? value : fallback;

    private sealed class CategoryManagerDialog : Window
    {
        private readonly M03ShellViewModel admin;
        private readonly ListBox list = null!;
        private readonly TextBox name = null!;
        private readonly TextBlock validation = null!;
        private readonly Button save = null!;
        private readonly Button cancel = null!;
        private readonly CategoryEditBuffer edit = new();
        private bool closeAllowed;

        public CategoryManagerDialog(Window owner, M03ShellViewModel admin)
        {
            this.admin = admin; Owner = owner; Width = 440; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, "ManageCategories", "Categories");
            var root = new DockPanel { Margin = new Thickness(14) };
            list = new ListBox { ItemsSource = admin.Categories, DisplayMemberPath = "Name", Height = 220 };
            list.SelectionChanged += (_, _) => { if (!edit.IsEditing && list.SelectedItem is CategorySummary category) name.Text = category.Name; };
            DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
            var heading = new TextBlock { Text = LocalizedText(owner, "CategoryEdit", "Category edit"), Margin = new Thickness(0, 10, 0, 2) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            name = new TextBox { Margin = new Thickness(0, 0, 0, 4) }; name.TextChanged += (_, _) => validation.Text = string.Empty; DockPanel.SetDock(name, Dock.Top); root.Children.Add(name);
            validation = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(validation, Dock.Top); root.Children.Add(validation);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var create = new Button { Content = LocalizedText(owner, "CreateCategory", "New"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) }; create.Click += (_, _) => { edit.BeginCreate(); name.Clear(); validation.Text = string.Empty; UpdateButtons(); };
            var rename = new Button { Content = LocalizedText(owner, "RenameCategory", "Rename"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) }; rename.Click += (_, _) => { if (list.SelectedItem is CategorySummary category) { edit.BeginRename(category.Id, category.Name); name.Text = category.Name; validation.Text = string.Empty; UpdateButtons(); } };
            save = new Button { Content = LocalizedText(owner, "Save", "Save"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), IsEnabled = false }; save.Click += Save;
            cancel = new Button { Content = LocalizedText(owner, "CategoryEditCancel", "Cancel"), Padding = new Thickness(10, 4, 10, 4), IsEnabled = false }; cancel.Click += (_, _) => { edit.Cancel(); name.Clear(); validation.Text = string.Empty; UpdateButtons(); };
            var close = new Button { Content = LocalizedText(owner, "Close", "Close"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) }; close.Click += (_, _) => { closeAllowed = true; DialogResult = true; Close(); };
            buttons.Children.Add(create); buttons.Children.Add(rename); buttons.Children.Add(save); buttons.Children.Add(cancel); buttons.Children.Add(close); root.Children.Add(buttons); Content = root;
            Closing += (_, _) => { if (!closeAllowed && edit.IsEditing) edit.Cancel(); };
        }

        private void UpdateButtons() { save.IsEnabled = edit.IsEditing; cancel.IsEnabled = edit.IsEditing; }
        private async void Save(object sender, RoutedEventArgs e)
        {
            edit.SetName(name.Text);
            var result = edit.CategoryId is { } id ? await admin.RenameCategoryAsync(id, edit.Name) : await admin.CreateCategoryAsync(edit.Name);
            if (!result.Succeeded) { validation.Text = M03Presentation.FormatIssues(result, ((ShellViewModel)Owner.DataContext).Localized); return; }
            await admin.RefreshAsync(); edit.Cancel(); name.Clear(); validation.Text = string.Empty; UpdateButtons();
        }
    }

    private sealed class ProductEditorDialog : Window
    {
        private readonly M03ShellViewModel admin; private readonly ProductDraft? existing; private readonly ComboBox category; private readonly TextBox code; private readonly TextBox productName; private readonly TextBox price; private readonly TextBox vat; private readonly CheckBox active; private readonly CheckBox discount; private readonly CheckBox options; private readonly StackPanel groupsPanel; private readonly TextBlock validation; private readonly List<GroupEditor> groups = [];
        private bool dirty; private bool closeAllowed;

        public ProductEditorDialog(Window owner, M03ShellViewModel admin, ProductDraft? existing, IReadOnlyList<CategorySummary> categories)
        {
            this.admin = admin; this.existing = existing; Owner = owner; Width = 560; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, existing is null ? "NewProduct" : "Edit", existing is null ? "New product" : "Edit");
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
            validation.Text = LocalizedText(this, "DirtyEditorClose", "Save or cancel your changes before closing.");
            MessageBox.Show(this, validation.Text, LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void AddGroup(OptionGroupDraft? draft) { var editor = new GroupEditor(this, draft); groups.Add(editor); groupsPanel.Children.Add(editor.Root); }
        private void RemoveGroup(GroupEditor editor) { dirty = true; groups.Remove(editor); groupsPanel.Children.Remove(editor.Root); }
        private void MoveGroup(GroupEditor editor, int delta) { dirty = true; var index = groups.IndexOf(editor); var target = index + delta; if (index < 0 || target < 0 || target >= groups.Count) return; (groups[index], groups[target]) = (groups[target], groups[index]); groupsPanel.Children.RemoveAt(index); groupsPanel.Children.Insert(target, editor.Root); }

        private sealed class GroupEditor
        {
            private readonly ProductEditorDialog owner; private readonly Guid id; private readonly TextBox name; private readonly ComboBox mode; private readonly CheckBox required; private readonly TextBox min; private readonly TextBox max; private readonly StackPanel optionsPanel; private readonly List<OptionEditor> options = [];
            public GroupEditor(ProductEditorDialog owner, OptionGroupDraft? draft)
            {
                this.owner = owner; id = draft?.Id ?? Guid.Empty; var border = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0) }; Root = new StackPanel(); border.Child = Root;
                name = AddText(Root, owner.Label("Name", "Name"), draft?.Name ?? string.Empty); name.TextChanged += (_, _) => owner.dirty = true;
                var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) }; modeRow.Children.Add(new TextBlock { Text = owner.Label("Options", "Mode"), Width = 90, VerticalAlignment = VerticalAlignment.Center }); mode = new ComboBox { Width = 150, ItemsSource = new[] { new ModeItem(DomainSelectionMode.Single, owner.Label("Single", "Single")), new ModeItem(DomainSelectionMode.Multi, owner.Label("Multi", "Multi")) }, DisplayMemberPath = "Label", SelectedValuePath = "Mode" }; mode.SelectedValue = draft?.SelectionMode ?? DomainSelectionMode.Single; mode.SelectionChanged += (_, _) => { owner.dirty = true; UpdateLimits(); }; modeRow.Children.Add(mode); Root.Children.Add(modeRow);
                required = new CheckBox { Content = owner.Label("Required", "Required"), IsChecked = draft?.IsRequired ?? false, Margin = new Thickness(0, 5, 0, 0) }; required.Checked += (_, _) => owner.dirty = true; required.Unchecked += (_, _) => owner.dirty = true; Root.Children.Add(required);
                var limits = new StackPanel { Orientation = Orientation.Horizontal }; limits.Children.Add(new TextBlock { Text = owner.Label("Minimum", "Min"), Width = 45, VerticalAlignment = VerticalAlignment.Center }); min = new TextBox { Width = 55, Text = draft?.MinSelections?.ToString(CultureInfo.InvariantCulture) ?? "0", Margin = new Thickness(0, 3, 8, 3) }; min.TextChanged += (_, _) => owner.dirty = true; limits.Children.Add(min); limits.Children.Add(new TextBlock { Text = owner.Label("Maximum", "Max"), Width = 50, VerticalAlignment = VerticalAlignment.Center }); max = new TextBox { Width = 55, Text = draft?.MaxSelections?.ToString(CultureInfo.InvariantCulture) ?? "1", Margin = new Thickness(0, 3, 0, 3) }; max.TextChanged += (_, _) => owner.dirty = true; limits.Children.Add(max); Root.Children.Add(limits);
                var groupButtons = new StackPanel { Orientation = Orientation.Horizontal }; var add = new Button { Content = "+ " + owner.Label("Options", "Option"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; add.Click += (_, _) => { owner.dirty = true; AddOption(null); }; var remove = new Button { Content = owner.Label("DeletePermanently", "Delete"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; remove.Click += (_, _) => owner.RemoveGroup(this); var up = new Button { Content = owner.Label("MoveUp", "Up"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; up.Click += (_, _) => owner.MoveGroup(this, -1); var down = new Button { Content = owner.Label("MoveDown", "Down"), Padding = new Thickness(6, 2, 6, 2) }; down.Click += (_, _) => owner.MoveGroup(this, 1); groupButtons.Children.Add(add); groupButtons.Children.Add(remove); groupButtons.Children.Add(up); groupButtons.Children.Add(down); Root.Children.Add(groupButtons);
                optionsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; Root.Children.Add(optionsPanel); foreach (var option in draft?.Options ?? []) AddOption(option); UpdateLimits();
            }
            public StackPanel Root { get; }
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
            private void MoveOption(OptionEditor option, int delta) { owner.dirty = true; var index = options.IndexOf(option); var target = index + delta; if (index < 0 || target < 0 || target >= options.Count) return; (options[index], options[target]) = (options[target], options[index]); optionsPanel.Children.RemoveAt(index); optionsPanel.Children.Insert(target, option.Root); }
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
                var delete = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 15, 0, 0) }; delete.Click += (_, _) => remove(); var up = new Button { Content = owner.Label("MoveUp", "↑"), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(5, 15, 0, 0) }; up.Click += (_, _) => move(-1); var down = new Button { Content = owner.Label("MoveDown", "↓"), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(2, 15, 0, 0) }; down.Click += (_, _) => move(1); Root.Children.Add(namePanel); Root.Children.Add(adjustmentPanel); Root.Children.Add(active); Root.Children.Add(up); Root.Children.Add(down); Root.Children.Add(delete);
            }
            public StackPanel Root { get; }
            public bool TryToDraft(int order, out OptionDraft? value, out ValidationIssue? issue)
            {
                if (!M03Presentation.TryParseMoney(adjustment.Text, "options", out var amount, out issue)) { value = null; return false; }
                value = new OptionDraft(id, name.Text, amount, active.IsChecked == true, order); return true;
            }
        }

        private sealed record ModeItem(DomainSelectionMode Mode, string Label);
        private string Label(string key, string fallback) => LocalizedText(this, key, fallback);
    }
}
