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
            MessageBox.Show(this, LocalizedText(this, "CreateCategoryFirst", "Create a category first."), "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (DataContext is not ShellViewModel { Admin: { } admin, Localized: { } labels } || admin.SelectedProduct is not { } product) return;
        var result = await admin.SetProductActiveAsync(product.Id, !product.IsActive);
        if (!result.Succeeded) ShowResultError(result); else await admin.RefreshAsync();
    }

    private async void OnDeleteProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin } || admin.SelectedProduct is not { } product) return;
        if (MessageBox.Show(this, $"{LocalizedText(this, "DeleteConfirm", "Delete this product permanently?")}\n\n{product.Code} — {product.Name}", "Sushi81 POS", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
            try { await admin.LoadSettingsAsync(); } catch { }
        }
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Admin: { } admin }) return;
        var result = await admin.SaveSettingsAsync();
        if (!result.Succeeded) ShowResultError(result); else MessageBox.Show(this, LocalizedText(this, "Saved", "Saved."), "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OnReloadSettings(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Admin: { } admin }) await admin.LoadSettingsAsync();
    }

    private void ShowResultError(OperationResult result) => MessageBox.Show(this, result.ErrorMessage ?? "The operation failed.", "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error);

    private static string LocalizedText(Window owner, string key, string fallback) => owner.DataContext is ShellViewModel viewModel && viewModel.Localized.TryGetValue(key, out var value) ? value : fallback;

    private sealed class CategoryManagerDialog : Window
    {
        private readonly M03ShellViewModel admin;
        private readonly ListBox list;
        private readonly TextBox name;
        public CategoryManagerDialog(Window owner, M03ShellViewModel admin)
        {
            this.admin = admin; Owner = owner; Width = 420; Height = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, "ManageCategories", "Categories");
            var root = new DockPanel { Margin = new Thickness(14) };
            list = new ListBox { ItemsSource = admin.Categories, DisplayMemberPath = "Name", Height = 220 }; DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
            name = new TextBox { Margin = new Thickness(0, 10, 0, 8) }; DockPanel.SetDock(name, Dock.Top); root.Children.Add(name);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var create = new Button { Content = LocalizedText(owner, "CreateCategory", "Create"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) }; create.Click += Create;
            var rename = new Button { Content = LocalizedText(owner, "RenameCategory", "Rename"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) }; rename.Click += Rename;
            var close = new Button { Content = LocalizedText(owner, "Close", "Close"), Padding = new Thickness(10, 4, 10, 4) }; close.Click += (_, _) => { DialogResult = true; Close(); };
            buttons.Children.Add(create); buttons.Children.Add(rename); buttons.Children.Add(close); root.Children.Add(buttons); Content = root;
        }
        private async void Create(object sender, RoutedEventArgs e) { var r = await admin.CreateCategoryAsync(name.Text); if (!r.Succeeded) MessageBox.Show(this, r.ErrorMessage); else { await admin.RefreshAsync(); name.Clear(); } }
        private async void Rename(object sender, RoutedEventArgs e) { if (list.SelectedItem is not CategorySummary category) return; var r = await admin.RenameCategoryAsync(category.Id, name.Text); if (!r.Succeeded) MessageBox.Show(this, r.ErrorMessage); else await admin.RefreshAsync(); }
    }

    private sealed class ProductEditorDialog : Window
    {
        private readonly M03ShellViewModel admin; private readonly ProductDraft? existing; private readonly ComboBox category; private readonly TextBox code; private readonly TextBox productName; private readonly TextBox price; private readonly TextBox vat; private readonly CheckBox active; private readonly CheckBox discount; private readonly CheckBox options; private readonly StackPanel groupsPanel; private readonly List<GroupEditor> groups = [];
        public ProductEditorDialog(Window owner, M03ShellViewModel admin, ProductDraft? existing, IReadOnlyList<CategorySummary> categories)
        {
            this.admin = admin; this.existing = existing; Owner = owner; Width = 500; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, existing is null ? "NewProduct" : "Edit", existing is null ? "New product" : "Edit");
            var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Margin = new Thickness(16) } }; var panel = (StackPanel)root.Content;
            code = AddText(panel, LocalizedText(owner, "Code", "Code"), existing?.Code ?? string.Empty); productName = AddText(panel, LocalizedText(owner, "Name", "Name"), existing?.Name ?? string.Empty);
            panel.Children.Add(new TextBlock { Text = LocalizedText(owner, "Category", "Category"), Margin = new Thickness(0, 8, 0, 2) }); category = new ComboBox { ItemsSource = categories, DisplayMemberPath = "Name", SelectedValuePath = "Id" }; category.SelectedValue = existing?.CategoryId ?? (categories.Count > 0 ? categories[0].Id : Guid.Empty); panel.Children.Add(category);
            price = AddText(panel, LocalizedText(owner, "PriceTtc", "TTC price"), existing?.PriceTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00"); vat = AddText(panel, LocalizedText(owner, "Vat", "VAT %"), existing?.VatRate.ToString(CultureInfo.InvariantCulture) ?? "10");
            active = AddCheck(panel, LocalizedText(owner, "Active", "Active"), existing?.IsActive ?? true); discount = AddCheck(panel, LocalizedText(owner, "DiscountEligible", "Retrait discount eligible"), existing?.DiscountEligible ?? true); options = AddCheck(panel, LocalizedText(owner, "OptionsEnabled", "Options enabled"), existing?.OptionsEnabled ?? false);
            panel.Children.Add(new TextBlock { Text = LocalizedText(owner, "OptionGroups", "Option groups"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            var addGroup = new Button { Content = "+ " + LocalizedText(owner, "OptionGroups", "Group"), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left }; addGroup.Click += (_, _) => AddGroup(null); panel.Children.Add(addGroup);
            groupsPanel = new StackPanel(); panel.Children.Add(groupsPanel);
            foreach (var group in existing?.Groups ?? []) AddGroup(group);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) }; var save = new Button { Content = LocalizedText(owner, "Save", "Save"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) }; save.Click += Save; var cancel = new Button { Content = LocalizedText(owner, "Cancel", "Cancel"), Padding = new Thickness(14, 5, 14, 5) }; cancel.Click += (_, _) => { DialogResult = false; Close(); }; buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = root;
        }
        private static TextBox AddText(Panel panel, string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2) }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
        private static CheckBox AddCheck(Panel panel, string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(box); return box; }
        private async void Save(object sender, RoutedEventArgs e)
        {
            if (category.SelectedValue is not Guid categoryId || !decimal.TryParse(price.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceValue) || !decimal.TryParse(vat.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var vatValue)) { MessageBox.Show(this, LocalizedText(this, "EnterValidValues", "Enter valid product values.")); return; }
            var draft = new ProductDraft(existing?.Id ?? Guid.Empty, code.Text, productName.Text, categoryId, Money.FromEuros(priceValue), vatValue, active.IsChecked == true, discount.IsChecked == true, options.IsChecked == true, groups.Select((group, index) => group.ToDraft(index)).ToArray());
            var result = existing is null ? await admin.CreateProductAsync(draft) : await admin.UpdateProductAsync(existing.Id, draft);
            if (!result.Succeeded) { MessageBox.Show(this, result.ErrorMessage); return; } DialogResult = true; Close();
        }

        private void AddGroup(OptionGroupDraft? draft)
        {
            var editor = new GroupEditor(this, draft); groups.Add(editor); groupsPanel.Children.Add(editor.Root);
        }

        private void RemoveGroup(GroupEditor editor) { groups.Remove(editor); groupsPanel.Children.Remove(editor.Root); }

        private void MoveGroup(GroupEditor editor, int delta)
        {
            var index = groups.IndexOf(editor);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= groups.Count) return;
            (groups[index], groups[target]) = (groups[target], groups[index]);
            groupsPanel.Children.RemoveAt(index);
            groupsPanel.Children.Insert(target, editor.Root);
        }

        private sealed class GroupEditor
        {
            private readonly ProductEditorDialog owner; private readonly Guid id; private readonly TextBox name; private readonly ComboBox mode; private readonly CheckBox required; private readonly TextBox min; private readonly TextBox max; private readonly StackPanel optionsPanel; private readonly List<OptionEditor> options = [];
            public GroupEditor(ProductEditorDialog owner, OptionGroupDraft? draft)
            {
                this.owner = owner; id = draft?.Id ?? Guid.Empty; var border = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0) }; Root = new StackPanel(); border.Child = Root;
                name = AddText(Root, owner.Label("Name", "Name"), draft?.Name ?? string.Empty);
                var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) }; modeRow.Children.Add(new TextBlock { Text = owner.Label("Options", "Mode"), Width = 90, VerticalAlignment = VerticalAlignment.Center }); mode = new ComboBox { Width = 150, ItemsSource = new[] { new ModeItem(DomainSelectionMode.Single, owner.Label("Single", "Single")), new ModeItem(DomainSelectionMode.Multi, owner.Label("Multi", "Multi")) }, DisplayMemberPath = "Label", SelectedValuePath = "Mode" }; mode.SelectedValue = draft?.SelectionMode ?? DomainSelectionMode.Single; mode.SelectionChanged += (_, _) => UpdateLimits(); modeRow.Children.Add(mode); Root.Children.Add(modeRow);
                required = new CheckBox { Content = owner.Label("Required", "Required"), IsChecked = draft?.IsRequired ?? false, Margin = new Thickness(0, 5, 0, 0) }; Root.Children.Add(required);
                var limits = new StackPanel { Orientation = Orientation.Horizontal }; limits.Children.Add(new TextBlock { Text = owner.Label("Minimum", "Min"), Width = 45, VerticalAlignment = VerticalAlignment.Center }); min = new TextBox { Width = 55, Text = draft?.MinSelections?.ToString(CultureInfo.InvariantCulture) ?? "0", Margin = new Thickness(0, 3, 8, 3) }; limits.Children.Add(min); limits.Children.Add(new TextBlock { Text = owner.Label("Maximum", "Max"), Width = 50, VerticalAlignment = VerticalAlignment.Center }); max = new TextBox { Width = 55, Text = draft?.MaxSelections?.ToString(CultureInfo.InvariantCulture) ?? "1", Margin = new Thickness(0, 3, 0, 3) }; limits.Children.Add(max); Root.Children.Add(limits);
                var groupButtons = new StackPanel { Orientation = Orientation.Horizontal }; var add = new Button { Content = "+ " + owner.Label("Options", "Option"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; add.Click += (_, _) => AddOption(null); var remove = new Button { Content = owner.Label("DeletePermanently", "Delete"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; remove.Click += (_, _) => owner.RemoveGroup(this); var up = new Button { Content = owner.Label("MoveUp", "Up"), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 2, 6, 2) }; up.Click += (_, _) => owner.MoveGroup(this, -1); var down = new Button { Content = owner.Label("MoveDown", "Down"), Padding = new Thickness(6, 2, 6, 2) }; down.Click += (_, _) => owner.MoveGroup(this, 1); groupButtons.Children.Add(add); groupButtons.Children.Add(remove); groupButtons.Children.Add(up); groupButtons.Children.Add(down); Root.Children.Add(groupButtons);
                optionsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; Root.Children.Add(optionsPanel); foreach (var option in draft?.Options ?? []) AddOption(option); UpdateLimits();
            }
            public StackPanel Root { get; }
            public OptionGroupDraft ToDraft(int order) => new(id, name.Text, (DomainSelectionMode)(mode.SelectedValue ?? DomainSelectionMode.Single), required.IsChecked == true, (DomainSelectionMode)(mode.SelectedValue ?? DomainSelectionMode.Single) == DomainSelectionMode.Multi && int.TryParse(min.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minValue) ? minValue : null, (DomainSelectionMode)(mode.SelectedValue ?? DomainSelectionMode.Single) == DomainSelectionMode.Multi && int.TryParse(max.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxValue) ? maxValue : null, order, options.Select((option, index) => option.ToDraft(index)).ToArray());
             private void AddOption(OptionDraft? draft) { OptionEditor? option = null; option = new OptionEditor(owner, draft, () => { if (option is not null) { options.Remove(option); optionsPanel.Children.Remove(option.Root); } }, delta => { if (option is not null) MoveOption(option, delta); }); options.Add(option); optionsPanel.Children.Add(option.Root); }
             private void MoveOption(OptionEditor option, int delta) { var index = options.IndexOf(option); var target = index + delta; if (index < 0 || target < 0 || target >= options.Count) return; (options[index], options[target]) = (options[target], options[index]); optionsPanel.Children.RemoveAt(index); optionsPanel.Children.Insert(target, option.Root); }
            private void UpdateLimits() { var multi = (DomainSelectionMode)(mode.SelectedValue ?? DomainSelectionMode.Single) == DomainSelectionMode.Multi; min.IsEnabled = multi; max.IsEnabled = multi; if (!multi) { min.Text = string.Empty; max.Text = string.Empty; } }
        }

        private sealed class OptionEditor
        {
            private readonly Guid id; private readonly TextBox name; private readonly TextBox adjustment; private readonly CheckBox active;
            public OptionEditor(ProductEditorDialog owner, OptionDraft? draft, Action remove, Action<int> move)
            {
                id = draft?.Id ?? Guid.Empty; Root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) }; name = new TextBox { Width = 130, Text = draft?.Name ?? string.Empty, ToolTip = owner.Label("Name", "Name") }; adjustment = new TextBox { Width = 70, Text = draft?.PriceAdjustmentTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00", Margin = new Thickness(5, 0, 5, 0), ToolTip = owner.Label("AdjustmentTtc", "Adjustment") }; active = new CheckBox { IsChecked = draft?.IsActive ?? true, VerticalAlignment = VerticalAlignment.Center }; var delete = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 0, 0, 0) }; delete.Click += (_, _) => remove(); var up = new Button { Content = owner.Label("MoveUp", "↑"), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(5, 0, 0, 0) }; up.Click += (_, _) => move(-1); var down = new Button { Content = owner.Label("MoveDown", "↓"), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(2, 0, 0, 0) }; down.Click += (_, _) => move(1); Root.Children.Add(name); Root.Children.Add(adjustment); Root.Children.Add(active); Root.Children.Add(up); Root.Children.Add(down); Root.Children.Add(delete);
            }
            public StackPanel Root { get; }
            public OptionDraft ToDraft(int order) => new(id, name.Text, decimal.TryParse(adjustment.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? Money.FromEuros(amount) : Money.Zero, active.IsChecked == true, order);
        }

        private sealed record ModeItem(DomainSelectionMode Mode, string Label);
        private string Label(string key, string fallback) => LocalizedText(this, key, fallback);
    }
}
