using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Configuration;
using DomainSelectionMode = Sushi81.Pos.Domain.SelectionMode;

namespace Sushi81.Pos.Desktop;

public partial class MainWindow : Window
{
    private bool loaded;
    private bool orderProductAddInProgress;
    private int commandesGridResizeInvocationCount;
    private int commandesGridWidthMutationCount;
    private IDisposable? performanceTraceProbe;
    private readonly MainWindowCloseCoordinator closeCoordinator;
    private readonly CatalogueHeaderSet catalogueHeaders = new();

    public MainWindow(ShellViewModel viewModel, IAsyncDisposable? recoveryScheduler = null, Action<Exception>? closeFailureLogger = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        if (viewModel.Admin is { } admin) admin.FilterRefreshFailed += OnFilterRefreshFailed;
        ApplyCatalogueHeaders();
        closeCoordinator = new MainWindowCloseCoordinator(
            () => viewModel.AuthorityState == WriteAuthorityState.Authoritative
                && viewModel.M07Runtime is not null,
            () => RequestCloseAsync(viewModel),
            targetDeviceId => TransferAndCloseAsync(viewModel, targetDeviceId),
            recoveryScheduler is null ? null : new Func<ValueTask>(recoveryScheduler.DisposeAsync),
            () => Dispatcher.BeginInvoke(new Action(Close)),
            exception =>
            {
                closeFailureLogger?.Invoke(exception);
                ShowCloseFailure(viewModel);
            });
        Closing += (_, closing) => _ = closeCoordinator.HandleClosingAsync(closing);
        Closed += OnClosed;
    }

    private async Task<MainWindowCloseRequest> RequestCloseAsync(ShellViewModel viewModel)
    {
        var choiceDialog = new AuthorityCloseChoiceDialog(this, viewModel.Localized);
        if (choiceDialog.ShowDialog() != true)
            return new MainWindowCloseRequest(MainWindowCloseIntent.Cancel);

        var choice = choiceDialog.Choice;
        if (choice.Intent != MainWindowCloseIntent.Transfer)
            return choice;

        // Target enumeration is deliberately after the user selected Transfer, so
        // Retain and Cancel never contact OneDrive/GitHub or alter authority state.
        if (viewModel.M07Runtime is not { } runtime || runtime.NormalHandoff is null)
        {
            ShowCloseFailure(viewModel, LocalizedText(this, "AuthorityTransferUnavailable", "Target-directed transfer is unavailable."));
            return new MainWindowCloseRequest(MainWindowCloseIntent.Cancel);
        }

        IReadOnlyList<DeviceRegistrationArtifact> targets;
        try
        {
            targets = await runtime.GetEligibleTransferTargetsAsync();
        }
        catch (Exception)
        {
            ShowCloseFailure(viewModel);
            return new MainWindowCloseRequest(MainWindowCloseIntent.Cancel);
        }

        var targetDialog = new AuthorityTargetSelectionDialog(this, viewModel.Localized, targets);
        return targetDialog.ShowDialog() == true && targetDialog.TargetDeviceId is { } target
            ? new MainWindowCloseRequest(MainWindowCloseIntent.Transfer, target)
            : new MainWindowCloseRequest(MainWindowCloseIntent.Cancel);
    }

    private async Task<bool> TransferAndCloseAsync(ShellViewModel viewModel, Guid targetDeviceId)
    {
        var runtime = viewModel.M07Runtime;
        if (runtime?.NormalHandoff is null)
        {
            ShowCloseFailure(viewModel, LocalizedText(this, "AuthorityTransferUnavailable", "Target-directed transfer is unavailable."));
            return false;
        }

        var result = await runtime.NormalHandoff.TransferAndCloseAsync(targetDeviceId);
        await viewModel.RefreshAuthorityStateAsync();
        if (!result.Succeeded)
        {
            // A failed handoff must leave the source visible. In particular, a
            // post-relinquishment pending state remains read-only/recovery-required.
            ShowCloseFailure(viewModel, LocalizedText(this, "AuthorityTransferFailed", "Authority transfer failed."));
            return false;
        }

        return true;
    }

    private void ShowCloseFailure(ShellViewModel viewModel, string? safeMessage = null)
    {
        var message = safeMessage ?? LocalizedText(this, "AuthorityTransferFailed", "Authority transfer failed.");
        MessageBox.Show(this, message, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded || DataContext is not ShellViewModel viewModel || viewModel.Admin is null && viewModel.Entry is null) return;
        loaded = true;
        performanceTraceProbe = PerformanceTrace.StartDispatcherGapProbe(Dispatcher);
        PerformanceTrace.Log("window.loaded");
        try
        {
            if (viewModel.Admin is { } admin) { PerformanceTrace.Log("m03.refresh.start"); await admin.RefreshAsync(); PerformanceTrace.Log("m03.refresh.end"); PerformanceTrace.Log("m03.settings.start"); await admin.LoadSettingsAsync(); PerformanceTrace.Log("m03.settings.end"); }
            if (viewModel.Entry is { } entry)
            {
                PerformanceTrace.Log("entry.refresh.start");
                await entry.RefreshAsync();
                PerformanceTrace.Log("entry.refresh.end");
            }
            if (viewModel.Lifecycle is { } lifecycle) { PerformanceTrace.Log("lifecycle.refresh.start"); await lifecycle.RefreshAsync(); PerformanceTrace.Log("lifecycle.refresh.end"); PerformanceTrace.Log("lifecycle.dashboard.start"); await lifecycle.RefreshDashboardAsync(); PerformanceTrace.Log("lifecycle.dashboard.end"); }
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
        ApplyCatalogueHeaders();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        performanceTraceProbe?.Dispose();
        PerformanceTrace.Log("window.closed");
        (DataContext as ShellViewModel)?.Dispose();
    }

    private async void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel || e.AddedItems.OfType<LanguageOption>().SingleOrDefault() is not { } language) return;
        try { await viewModel.ChangeLanguageAsync(language); ApplyCatalogueHeaders(); }
        catch { MessageBox.Show(this, viewModel.LanguageSaveFailure, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void OnJoinExistingLineage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanJoinExistingLineage: true } viewModel) return;
        var dialog = new DeviceJoinDialog(this, viewModel.Localized);
        if (dialog.ShowDialog() != true) return;

        try
        {
            await viewModel.JoinExistingLineageAsync(dialog.DisplayName);
            MessageBox.Show(this, LocalizedText(this, "JoinSucceeded", "This computer is paired read-only."), viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnAcquireTransferredAuthority(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanAcquireTransferredAuthority: true } viewModel) return;
        try
        {
            var result = await viewModel.AcquireTransferredAuthorityAsync();
            if (result is { Succeeded: false })
                MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch
        {
            MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnResumePendingTransfer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanResumePendingTransfer: true } viewModel) return;
        try
        {
            var result = await viewModel.ResumePendingTransferAsync();
            if (result is { Succeeded: false })
                MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch
        {
            MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnStartDisasterRecovery(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanStartDisasterRecovery: true } viewModel) return;
        try
        {
            var discovered = await viewModel.DiscoverRecoveryCandidatesAsync();
            if (discovered is null || discovered.Candidates.Count == 0)
            {
                MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new DisasterRecoveryDialog(this, viewModel.Localized, discovered.Candidates, discovered.Recommended);
            if (dialog.ShowDialog() != true) return;
            var result = await viewModel.StartDisasterRecoveryAsync(dialog.SelectedCandidateId!, dialog.QuarantineConfirmed);
            if (result is { Succeeded: false })
                MessageBox.Show(this, result.Diagnostic, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRetryDisasterRecovery(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanRetryDisasterRecovery: true } viewModel) return;
        var dialog = new DisasterRecoveryDialog(this, viewModel.Localized, Array.Empty<RecoveryCandidate>(), null);
        if (dialog.ShowDialog() != true) return;
        try
        {
            var result = await viewModel.RetryDisasterRecoveryAsync(dialog.QuarantineConfirmed);
            if (result is { Succeeded: false })
                MessageBox.Show(this, result.Diagnostic, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnReinitializeStaleDevice(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanReinitializeStaleDevice: true } viewModel) return;
        try
        {
            var result = await viewModel.ReinitializeStaleDeviceAsync();
            if (result is not null)
                MessageBox.Show(this, result.Diagnostic, viewModel.Title, MessageBoxButton.OK,
                    result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnTestGitHubConnection(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanTestGitHubConnection: true } viewModel) return;
        try
        {
            await viewModel.TestGitHubConnectionAsync();
            MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch
        {
            MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnConfigureM07(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { CanConfigureM07: true } viewModel) return;

        var dialog = new M07SetupDialog(this, viewModel.Localized, viewModel.Configuration);
        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = await viewModel.ConfigureM07Async(dialog.Input);
            if (result is { Succeeded: true })
            {
                MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result is not null)
            {
                MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            MessageBox.Show(this, viewModel.M07OperationStatus, viewModel.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyCatalogueHeaders()
    {
        if (DataContext is not ShellViewModel viewModel) return;
        if (catalogueGrid.Columns.Count >= 6)
        {
            catalogueHeaders.Apply(viewModel.Localized);
            var values = catalogueHeaders.Values;
            for (var index = 0; index < values.Count; index++) catalogueGrid.Columns[index].Header = values[index];
        }
        if (orderProductsGrid.Columns.Count >= 3)
        {
            orderProductsGrid.Columns[0].Header = LocalizedText(this, "Code", "Code");
            orderProductsGrid.Columns[1].Header = LocalizedText(this, "Name", "Name");
            orderProductsGrid.Columns[2].Header = LocalizedText(this, "PriceTtc", "TTC price");
        }
        if (commandesGrid.Columns.Count >= 9)
        {
            commandesGrid.Columns[0].Header = LocalizedText(this, "OrderReference", "Reference");
            commandesGrid.Columns[1].Header = LocalizedText(this, "PlannedDate", "Date");
            commandesGrid.Columns[2].Header = LocalizedText(this, "PlannedTime", "Time");
            commandesGrid.Columns[3].Header = LocalizedText(this, "OrderStatus", "Status");
            commandesGrid.Columns[4].Header = LocalizedText(this, "TotalTtc", "Total TTC");
            commandesGrid.Columns[5].Header = LocalizedText(this, "Telephone", "Telephone");
            commandesGrid.Columns[6].Header = LocalizedText(this, "Fulfilment", "Mode");
            commandesGrid.Columns[7].Header = LocalizedText(this, "Comment", "Comment");
            commandesGrid.Columns[8].Header = LocalizedText(this, "DeliveryAddress", "Address");
        }
    }

    private void OnCommandesGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PerformanceTrace.Log("commandes.grid.size-changed");
        if (sender is DataGrid grid) ResizeCommandesColumns(grid);
    }

    private void OnCommandesGridLoaded(object sender, RoutedEventArgs e)
    {
        PerformanceTrace.Log("commandes.grid.loaded");
        if (sender is DataGrid grid) ResizeCommandesColumns(grid);
    }

    private void OnMainWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Window || commandesGrid.ActualWidth <= 0) return;
        PerformanceTrace.Log("window.size-changed");
        var widthDelta = e.NewSize.Width - e.PreviousSize.Width;
        var estimatedGridWidth = commandesGrid.ActualWidth + widthDelta;
        var targetGridWidth = estimatedGridWidth <= ActualWidth ? estimatedGridWidth : commandesGrid.ActualWidth;
        ResizeCommandesColumns(commandesGrid, targetGridWidth);
    }

    private void OnMainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl || mainTabs.SelectedItem is not TabItem selected) return;
        var key = mainTabs.Items.IndexOf(selected) switch
        {
            0 => "catalogue",
            1 => "settings",
            2 => "commandes",
            3 => "caisse",
            _ => "settings"
        };
        PerformanceTrace.Log($"tab.selected.{key}");
    }

    private void ResizeCommandesColumns(DataGrid grid, double? targetWidth = null)
    {
        commandesGridResizeInvocationCount++;
        if (grid.Columns.Count < 9) return;
        var fixedWidth = grid.Columns.Take(7).Sum(column => column.ActualWidth);
        var availableForLongText = (targetWidth ?? grid.ActualWidth) - 2 - fixedWidth;
        if (availableForLongText < grid.Columns[7].MinWidth + grid.Columns[8].MinWidth) return;
        var longTextWidth = availableForLongText / 2;
        for (var index = 7; index <= 8; index++)
        {
            if (Math.Abs(grid.Columns[index].ActualWidth - longTextWidth) > 0.5)
            {
                grid.Columns[index].Width = new DataGridLength(longTextWidth, DataGridLengthUnitType.Pixel);
                commandesGridWidthMutationCount++;
                PerformanceTrace.Log("commandes.grid.width-mutated");
            }
        }
    }

    private async void OnRefreshCatalogue(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Admin: { } admin }) await admin.RefreshAsync();
    }

    private async void OnAddOrderProduct(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Entry: { } entry }) return;
        if (orderProductAddInProgress) return;
        if (ReferenceEquals(sender, orderProductsGrid))
        {
            if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { DataContext: ProductSummary productSummary }) return;
            entry.SelectedProduct = productSummary;
        }
        if (!entry.CanAddSelectedProduct) return;
        orderProductAddInProgress = true;
        try
        {
            await entry.AddSelectedProductAsync();
            if (entry.PendingProduct is { } product)
            {
                if (!product.Aggregate.Product.OptionsEnabled)
                {
                    entry.AddConfiguredLine(product, [], [], 1);
                    return;
                }

                var dialog = new OptionSelectionDialog(this, product, null);
                if (dialog.ShowDialog() == true) entry.AddConfiguredLine(product, dialog.SelectedOptionIds, dialog.CustomAdjustments, dialog.Quantity);
            }
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { orderProductAddInProgress = false; }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
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
            var result = dialog.ShowDialog();
            if (dialog.RemoveRequested) entry.RemoveLine(line);
            else if (result == true) entry.UpdateConfiguredLine(line, dialog.SelectedOptionIds, dialog.CustomAdjustments, dialog.Quantity);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Sushi81 POS", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnDecreaseOrderQuantity(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry } && (sender as Button)?.Tag is OrderEntryCartLineViewModel line)
            entry.ChangeQuantity(line, line.Quantity - 1);
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

    private async void OnRefreshOrderBrowser(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry }) await entry.RefreshOrderBrowserAsync();
    }

    private void OnOrderBrowserSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel { Entry: { } entry } && e.AddedItems.OfType<OrderBrowserRowViewModel>().LastOrDefault() is { } row)
            _ = entry.SelectBrowserOrderAsync(row);
    }

    private async void OnRefreshCommandes(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle }) await lifecycle.RefreshAsync();
    }

    private async void OnRefreshDashboard(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle }) await lifecycle.RefreshDashboardAsync();
    }

    private void OnDashboardEntry(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle }) return;
        lifecycle.SelectOperationalView((sender as Button)?.Tag?.ToString());
        mainTabs.SelectedItem = mainTabs.Items.OfType<TabItem>().FirstOrDefault(item => item.DataContext is OrderLifecycleShellViewModel);
    }

    private void OnBrowseByDate(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle }) lifecycle.ReturnToDateBrowse();
    }

    private async void OnCommandesSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle } && IsLoaded) await lifecycle.RefreshAsync();
    }

    private void OnModifyOrder(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle }) lifecycle.BeginModification();
    }

    private async void OnSaveOrderModification(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle }) await lifecycle.SaveModificationAsync();
    }

    private void OnAbandonOrderModification(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle }) lifecycle.AbandonModification();
    }

    private async void OnAddCurrentOrderLine(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle, Entry: { } entry } || !lifecycle.CanAddCurrentLine) return;
        var products = await entry.ListActiveProductsAsync();
        var picker = new CatalogueProductPickerDialog(this, products);
        if (picker.ShowDialog() != true || picker.SelectedProduct is not { } summary) return;
        var product = await entry.GetActiveProductForEditAsync(summary.Id);
        if (product is null) return;
        var dialog = new OptionSelectionDialog(this, product, null);
        if (dialog.ShowDialog() == true)
            await lifecycle.AddCurrentCatalogueLineAsync(new OrderLineDraft(Guid.Empty, product.Aggregate, dialog.SelectedOptionIds, dialog.CustomAdjustments, dialog.Quantity, product.CategoryName));
    }

    private async void OnReconfigureOrderLine(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle, Entry: { } entry } || sender is not Button { Tag: OrderDetailLineViewModel line } || !lifecycle.IsEditing) return;

        var choice = new ExistingLineEditDialog(this, line, canReconfigure: true);
        var choiceResult = choice.ShowDialog();
        if (choice.RemoveRequested)
        {
            lifecycle.RemoveLine(line);
            return;
        }
        if (choiceResult != true) return;
        if (choice.QuantityOnly)
        {
            lifecycle.UpdateLineQuantity(line, choice.Quantity);
            return;
        }

        OrderEntryProduct? product = null;
        if (line.Item.SourceProductId is { } productId)
            product = await entry.GetActiveProductForEditAsync(productId);
        if (product is null)
        {
            MessageBox.Show(this, LocalizedText(this, "ProductInactive", "Le produit n’est plus actif."));
            return;
        }

        var draft = line.ToCurrentDraft(product);
        var dialog = new OptionSelectionDialog(this, product, choice.Quantity, draft.SelectedOptionIds, draft.CustomAdjustments);
        var dialogResult = dialog.ShowDialog();
        if (dialog.RemoveRequested)
        {
            lifecycle.RemoveLine(line);
            return;
        }
        if (dialogResult == true)
            if (line.HasSameConfiguration(dialog.SelectedOptionIds, dialog.CustomAdjustments))
                lifecycle.UpdateLineQuantity(line, dialog.Quantity);
            else
                await lifecycle.ReplaceLineAsync(line, draft with { SelectedOptionIds = dialog.SelectedOptionIds, CustomAdjustments = dialog.CustomAdjustments, Quantity = dialog.Quantity }, default);
    }

    private void OnRemoveLifecycleLine(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle } && sender is Button { Tag: OrderDetailLineViewModel line }) lifecycle.RemoveLine(line);
    }

    private void OnDecreaseLifecycleQuantity(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle } || sender is not Button { Tag: OrderDetailLineViewModel line } || !lifecycle.IsEditing) return;
        if (line.Quantity <= 1) lifecycle.RemoveLine(line);
        else lifecycle.UpdateLineQuantity(line, line.Quantity - 1);
    }

    private void OnIncreaseLifecycleQuantity(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Lifecycle: { } lifecycle } && sender is Button { Tag: OrderDetailLineViewModel line } && lifecycle.IsEditing)
            lifecycle.UpdateLineQuantity(line, line.Quantity + 1);
    }

    private async void OnCloseOrder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle }) return;
        if (MessageBox.Show(this, LocalizedText(this, "OrderClose", "Clôturer cette commande ?"), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await lifecycle.CloseSelectedAsync();
    }

    private async void OnCancelOrder(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle }) return;
        if (MessageBox.Show(this, LocalizedText(this, "OrderCancel", "Annuler cette commande ?"), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await lifecycle.CancelSelectedAsync();
    }

    private void OnReuseOrderCustomer(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { Lifecycle: { } lifecycle, Entry: { } entry } || lifecycle.SelectedOrder is not { } order) return;
        if (entry.HasUncommittedDraft && MessageBox.Show(this, LocalizedText(this, "Discard", "Remplacer le brouillon Caisse en cours ?"), LocalizedText(this, "ShellTitle", "Sushi81 POS"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        entry.StartNewOrderFromCustomer(order);
        mainTabs.SelectedItem = mainTabs.Items.OfType<TabItem>().FirstOrDefault(item => item.DataContext is OrderEntryShellViewModel);
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
        private TextBox quantity = null!;
        private readonly IReadOnlyDictionary<string, string> localized;
        public OptionSelectionDialog(Window owner, OrderEntryProduct product, OrderEntryCartLineViewModel? existing)
            : this(owner, product, existing?.Quantity ?? 1, existing?.Draft.SelectedOptionIds ?? [], existing?.Draft.CustomAdjustments ?? []) { }

        public OptionSelectionDialog(Window owner, OrderEntryProduct product, int quantityValue, IReadOnlyList<Guid> existingOptions, IReadOnlyList<OrderLineAdjustmentDraft> existingCustomAdjustments)
        {
            this.product = product;
            localized = (owner.DataContext as ShellViewModel)?.Localized ?? new Dictionary<string, string>();
            Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = Label("Options", "Options"); Width = 520; Height = 620; MinHeight = 420;
            var root = new DockPanel { Margin = new Thickness(14) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = Label("Cancel", "Annuler"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => DialogResult = false;
            var ok = new Button { Content = Label("Add", "Ajouter"), Padding = new Thickness(12, 5, 12, 5) }; ok.Click += (_, _) => Accept(); buttons.Children.Add(cancel); buttons.Children.Add(ok); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = $"{product.Aggregate.Product.Code} — {product.Aggregate.Product.Name}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
            AddQuantityEditor(panel, quantityValue);
            if (product.Aggregate.Product.OptionsEnabled)
                foreach (var group in product.Aggregate.Groups.OrderBy(group => group.DisplayOrder)) AddGroup(panel, group, existingOptions);
            panel.Children.Add(new TextBlock { Text = Label("CustomAdjustments", "Ajustements personnalisés (par unité)"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var current in existingCustomAdjustments) AddCustomRow(current);
            var addCustom = new Button { Content = "+ " + Label("AddAdjustment", "Ajustement"), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) }; addCustom.Click += (_, _) => AddCustomRow(null);
            panel.Children.Add(customPanel); panel.Children.Add(addCustom);
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        }

        public IReadOnlyList<Guid> SelectedOptionIds { get; private set; } = [];
        public IReadOnlyList<OrderLineAdjustmentDraft> CustomAdjustments { get; private set; } = [];
        public int Quantity { get; private set; }
        public bool RemoveRequested { get; private set; }

        private void AddQuantityEditor(Panel panel, int quantityValue)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = Label("Quantity", "Quantité"), Width = 150, VerticalAlignment = VerticalAlignment.Center });
            quantity = new TextBox { Width = 70, Text = quantityValue.ToString(CultureInfo.InvariantCulture) };
            NumericInputBehavior.SetSelectAllOnFocus(quantity, true);
            var decrease = new Button { Content = "−", Width = 28, Height = 26, Margin = new Thickness(6, 0, 2, 0) };
            var increase = new Button { Content = "+", Width = 28, Height = 26 };
            decrease.Click += (_, _) => AdjustQuantity(-1);
            increase.Click += (_, _) => AdjustQuantity(1);
            row.Children.Add(quantity);
            row.Children.Add(decrease);
            row.Children.Add(increase);
            panel.Children.Add(row);
        }

        private void AdjustQuantity(int delta)
        {
            var current = int.TryParse(quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 1;
            var next = current + delta;
            if (next > 0)
            {
                quantity.Text = next.ToString(CultureInfo.InvariantCulture);
                return;
            }

            if (delta < 0 && current == 1)
            {
                RemoveRequested = true;
                DialogResult = false;
            }
        }

        private void AddCustomRow(OrderLineAdjustmentDraft? current)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var label = new TextBox { Width = 210, ToolTip = Label("AdjustmentLabel", "Libellé"), Text = current?.Label ?? string.Empty };
            var amount = new TextBox { Width = 90, Margin = new Thickness(6, 0, 0, 0), ToolTip = Label("AdjustmentAmount", "Montant TTC"), Text = current is null ? string.Empty : current.AmountTtcPerUnit.Euros.ToString("0.00", CultureInfo.InvariantCulture) };
            NumericInputBehavior.SetSelectAllOnFocus(amount, true);
            var remove = new Button { Content = "×", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(5, 2, 5, 2) };
            var tuple = (label, amount, remove); remove.Click += (_, _) => { customPanel.Children.Remove(row); customRows.Remove(tuple); }; row.Children.Add(label); row.Children.Add(amount); row.Children.Add(remove); customPanel.Children.Add(row); customRows.Add(tuple);
        }

        private void AddGroup(Panel panel, OptionGroup group, IReadOnlyList<Guid> existing)
        {
            var title = $"{group.Name} — {(group.SelectionMode == DomainSelectionMode.Single ? Label("Single", "SINGLE") : $"{Label("Multi", "MULTI")} {group.MinSelections}-{group.MaxSelections}")} {(group.IsRequired ? Label("Required", "requis") : Label("Optional", "optionnel"))}";
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            var list = new List<FrameworkElement>(); controls[group.Id] = list;
            foreach (var option in (product.Aggregate.OptionsByGroup.TryGetValue(group.Id, out var values) ? values : []).Where(option => option.IsActive || existing.Contains(option.Id)).OrderBy(option => option.DisplayOrder))
            {
                FrameworkElement control = group.SelectionMode == DomainSelectionMode.Single
                    ? new RadioButton { Content = $"{option.Name} ({option.PriceAdjustmentTtc.Euros:0.00} €)", GroupName = $"group-{group.Id}", IsChecked = existing.Contains(option.Id), Tag = option.Id }
                    : new CheckBox { Content = $"{option.Name} ({option.PriceAdjustmentTtc.Euros:0.00} €)", IsChecked = existing.Contains(option.Id), Tag = option.Id };
                list.Add(control); panel.Children.Add(control);
            }
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
                if (label.Length == 0 || !M03Presentation.TryParseDecimalInput(row.Amount.Text, out var amount)) { MessageBox.Show(this, Label("InvalidAdjustment", "Chaque ajustement doit avoir un libellé et un montant valides.")); return; }
                custom.Add(new(null, null, label, Money.FromEuros(amount), OrderAdjustmentKind.CustomAdjustment, custom.Count));
            }
            SelectedOptionIds = selected; CustomAdjustments = custom; Quantity = parsedQuantity; DialogResult = true;
        }

        private string Label(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
    }

    private sealed class ExistingLineEditDialog : Window
    {
        private TextBox quantity = null!;
        private readonly IReadOnlyDictionary<string, string> localized;
        private readonly bool canReconfigure;

        public ExistingLineEditDialog(Window owner, OrderDetailLineViewModel line, bool canReconfigure)
        {
            localized = (owner.DataContext as ShellViewModel)?.Localized ?? new Dictionary<string, string>();
            this.canReconfigure = canReconfigure;
            Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = Label("OrderEdit", "Modifier"); Width = 440; Height = 220; MinHeight = 180;
            var root = new DockPanel { Margin = new Thickness(14) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = Label("Cancel", "Annuler"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => DialogResult = false;
            var saveQuantity = new Button { Content = Label("OrderSave", "Enregistrer la modification"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            saveQuantity.Click += (_, _) => Accept(quantityOnly: true);
            buttons.Children.Add(cancel);
            buttons.Children.Add(saveQuantity);
            if (canReconfigure)
            {
                var options = new Button { Content = Label("Options", "Choix"), Padding = new Thickness(12, 5, 12, 5) };
                options.Click += (_, _) => Accept(quantityOnly: false);
                buttons.Children.Add(options);
            }
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = line.ProductText, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            AddQuantityEditor(panel, line.Quantity);
            root.Children.Add(panel);
            Content = root;
        }

        public bool QuantityOnly { get; private set; }
        public int Quantity { get; private set; }
        public bool RemoveRequested { get; private set; }

        private void AddQuantityEditor(Panel panel, int quantityValue)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = Label("Quantity", "Quantité"), Width = 150, VerticalAlignment = VerticalAlignment.Center });
            quantity = new TextBox { Width = 70, Text = quantityValue.ToString(CultureInfo.InvariantCulture) };
            NumericInputBehavior.SetSelectAllOnFocus(quantity, true);
            var decrease = new Button { Content = "−", Width = 28, Height = 26, Margin = new Thickness(6, 0, 2, 0) };
            var increase = new Button { Content = "+", Width = 28, Height = 26 };
            decrease.Click += (_, _) => AdjustQuantity(-1);
            increase.Click += (_, _) => AdjustQuantity(1);
            row.Children.Add(quantity);
            row.Children.Add(decrease);
            row.Children.Add(increase);
            panel.Children.Add(row);
        }

        private void AdjustQuantity(int delta)
        {
            var current = int.TryParse(quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 1;
            var next = current + delta;
            if (next > 0)
            {
                quantity.Text = next.ToString(CultureInfo.InvariantCulture);
                return;
            }

            if (delta < 0 && current == 1)
            {
                RemoveRequested = true;
                DialogResult = false;
            }
        }

        private void Accept(bool quantityOnly)
        {
            if (!int.TryParse(quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuantity) || parsedQuantity <= 0)
            {
                MessageBox.Show(this, Label("InvalidQuantity", "La quantité doit être un entier positif."));
                return;
            }
            QuantityOnly = quantityOnly;
            Quantity = parsedQuantity;
            DialogResult = true;
        }

        private string Label(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
    }

    private sealed class CatalogueProductPickerDialog : Window
    {
        private readonly ListBox products;
        public CatalogueProductPickerDialog(Window owner, IReadOnlyList<ProductSummary> values)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = LocalizedText(owner, "Products", "Produits");
            Width = 420;
            Height = 360;
            MinWidth = 320;
            MinHeight = 240;

            var root = new DockPanel { Margin = new Thickness(12) };
            var add = new Button
            {
                Content = LocalizedText(owner, "Add", "Ajouter"),
                Padding = new Thickness(10, 4, 10, 4),
                MinWidth = 84,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            DockPanel.SetDock(add, Dock.Bottom);
            root.Children.Add(add);

            var source = values.ToList();
            var view = new System.Windows.Data.ListCollectionView(source);
            var list = new ListBox
            {
                ItemsSource = view,
                MinHeight = 120,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsSynchronizedWithCurrentItem = false,
                SelectedIndex = -1
            };
            list.Loaded += (_, _) => list.SelectedIndex = -1;
            var search = new TextBox
            {
                MinHeight = 26,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = LocalizedText(owner, "Search", "Search")
            };
            search.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, LocalizedText(owner, "Search", "Search"));
            view.Filter = item =>
            {
                if (item is not ProductSummary product) return false;
                var query = search.Text.Trim();
                return query.Length == 0
                    || product.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || product.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            };
            search.TextChanged += (_, _) =>
            {
                var selected = list.SelectedItem;
                view.Refresh();
                if (selected is null || !view.Contains(selected)) list.SelectedIndex = -1;
            };
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var code = new FrameworkElementFactory(typeof(TextBlock));
            code.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            code.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            code.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProductSummary.Code)));
            row.AppendChild(code);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProductSummary.Name)));
            row.AppendChild(name);

            list.ItemTemplate = new DataTemplate { VisualTree = row };
            products = list;
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) { DialogResult = true; Close(); } };
            add.Click += (_, _) => { if (list.SelectedItem is not null) { DialogResult = true; Close(); } };
            DockPanel.SetDock(search, Dock.Top);
            root.Children.Add(search);
            root.Children.Add(list);
            Content = root;
        }
        public ProductSummary? SelectedProduct => products.SelectedItem as ProductSummary;
    }

    private static string LocalizedText(Window owner, string key, string fallback) => owner.DataContext is ShellViewModel viewModel && viewModel.Localized.TryGetValue(key, out var value) ? value : fallback;

    private sealed class CategoryManagerDialog : Window
    {
        private readonly M03ShellViewModel admin;
        private readonly ListBox list = null!;
        private readonly TextBox name = null!;
        private readonly TextBox shortCode = null!;
        private readonly TextBlock validation = null!;
        private readonly Button create = null!;
        private readonly Button rename = null!;
        private readonly Button save = null!;
        private readonly Button cancel = null!;
        private readonly Button close = null!;
        private readonly CategoryEditBuffer edit = new();
        private bool closeAllowed;
        private bool saveInProgress;

        public CategoryManagerDialog(Window owner, M03ShellViewModel admin)
        {
            this.admin = admin; Owner = owner; Width = 440; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner; Title = LocalizedText(owner, "ManageCategories", "Categories");
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            list = new ListBox { ItemsSource = admin.Categories, DisplayMemberPath = "MaintenanceLabel", MinHeight = 120, VerticalAlignment = VerticalAlignment.Stretch };
            list.SelectionChanged += (_, _) => { if (!edit.IsEditing && list.SelectedItem is CategorySummary category) { name.Text = category.Name; shortCode.Text = category.ShortCode ?? string.Empty; } UpdateButtons(); };
            Grid.SetRow(list, 0); root.Children.Add(list);
            var heading = new TextBlock { Text = LocalizedText(owner, "CategoryEdit", "Category edit"), Margin = new Thickness(0, 10, 0, 2) }; Grid.SetRow(heading, 1); root.Children.Add(heading);
            name = new TextBox { Margin = new Thickness(0, 0, 0, 4), IsEnabled = false }; name.TextChanged += (_, _) => validation.Text = string.Empty; Grid.SetRow(name, 2); root.Children.Add(name);
            var shortCodeLabel = new TextBlock { Text = LocalizedText(owner, "CategoryShortCode", "Short code"), Margin = new Thickness(0, 4, 0, 2) }; Grid.SetRow(shortCodeLabel, 3); root.Children.Add(shortCodeLabel);
            shortCode = new TextBox { Margin = new Thickness(0, 0, 0, 4), IsEnabled = false, ToolTip = LocalizedText(owner, "CategoryShortCodeTooltip", "Short code") }; shortCode.TextChanged += (_, _) => validation.Text = string.Empty; Grid.SetRow(shortCode, 4); root.Children.Add(shortCode);
            validation = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; Grid.SetRow(validation, 5); root.Children.Add(validation);
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
            create = new Button { Content = LocalizedText(owner, "CreateCategory", "New"), MinWidth = 84, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top }; create.Click += (_, _) => { edit.BeginCreate(); name.IsEnabled = true; shortCode.IsEnabled = true; name.Clear(); shortCode.Clear(); validation.Text = string.Empty; UpdateButtons(); FocusName(selectAll: false); };
            rename = new Button { Content = LocalizedText(owner, "RenameCategory", "Rename"), MinWidth = 96, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top, IsEnabled = false }; rename.Click += (_, _) => { if (list.SelectedItem is CategorySummary category) { edit.BeginRename(category.Id, category.Name, category.ShortCode); name.IsEnabled = true; shortCode.IsEnabled = true; name.Text = category.Name; shortCode.Text = category.ShortCode ?? string.Empty; validation.Text = string.Empty; UpdateButtons(); FocusName(selectAll: true); } };
            save = new Button { Content = LocalizedText(owner, "Save", "Save"), MinWidth = 112, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top, IsEnabled = false }; save.Click += Save;
            cancel = new Button { Content = LocalizedText(owner, "CategoryEditCancel", "Cancel"), MinWidth = 172, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top, IsEnabled = false }; cancel.Click += (_, _) => { edit.Cancel(); name.Clear(); shortCode.Clear(); validation.Text = string.Empty; UpdateButtons(); };
            close = new Button { Content = LocalizedText(owner, "Close", "Close"), MinWidth = 84, MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Top }; close.Click += (_, _) => { closeAllowed = true; DialogResult = true; Close(); };
            buttons.Children.Add(create); buttons.Children.Add(rename); buttons.Children.Add(save); buttons.Children.Add(cancel); buttons.Children.Add(close); Grid.SetRow(buttons, 6); root.Children.Add(buttons); Content = root;
            UpdateButtons();
            Closing += (_, _) => { if (!closeAllowed && edit.IsEditing) edit.Cancel(); };
        }

        private void UpdateButtons()
        {
            var editing = edit.IsEditing;
            create.IsEnabled = edit.CanBeginEdit && !saveInProgress;
            rename.IsEnabled = edit.CanBeginEdit && !saveInProgress && list.SelectedItem is CategorySummary;
            name.IsEnabled = editing && !saveInProgress;
            shortCode.IsEnabled = editing && !saveInProgress;
            save.IsEnabled = edit.CanSave && !saveInProgress;
            cancel.IsEnabled = edit.CanCancel && !saveInProgress;
            close.IsEnabled = !saveInProgress;
        }

        private void FocusName(bool selectAll)
        {
            name.Focus();
            Keyboard.Focus(name);
            if (selectAll) name.SelectAll(); else name.CaretIndex = name.Text.Length;
        }

        private async void Save(object sender, RoutedEventArgs e)
        {
            if (saveInProgress) return;
            saveInProgress = true;
            UpdateButtons();
            try
            {
                edit.SetName(name.Text);
                edit.SetShortCode(shortCode.Text);
                var result = edit.CategoryId is { } id ? await admin.RenameCategoryWithCodeAsync(id, edit.Name, edit.ShortCode) : await admin.CreateCategoryWithCodeAsync(edit.Name, edit.ShortCode);
                if (!result.Succeeded) { validation.Text = M03Presentation.FormatIssues(result, ((ShellViewModel)Owner.DataContext).Localized); return; }
                await admin.RefreshAsync(); edit.CompleteSave(); name.Clear(); shortCode.Clear(); validation.Text = string.Empty; list.Focus();
            }
            catch (Exception exception) { validation.Text = exception.Message; }
            finally { saveInProgress = false; UpdateButtons(); }
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
            price = AddText(panel, LocalizedText(owner, "PriceTtc", "TTC price"), existing?.PriceTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00"); vat = AddText(panel, LocalizedText(owner, "Vat", "VAT %"), existing?.VatRate.ToString(CultureInfo.InvariantCulture) ?? "10"); NumericInputBehavior.SetSelectAllOnFocus(price, true); NumericInputBehavior.SetSelectAllOnFocus(vat, true);
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
                var limits = new WrapPanel { Orientation = Orientation.Horizontal }; limits.Children.Add(new TextBlock { Text = owner.Label("Minimum", "Min"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 4, 3) }); min = new TextBox { Width = 55, Text = draft?.MinSelections?.ToString(CultureInfo.InvariantCulture) ?? "0", Margin = new Thickness(0, 3, 8, 3) }; NumericInputBehavior.SetSelectAllOnFocus(min, true); limits.Children.Add(min); limits.Children.Add(new TextBlock { Text = owner.Label("Maximum", "Max"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 4, 3) }); max = new TextBox { Width = 55, Text = draft?.MaxSelections?.ToString(CultureInfo.InvariantCulture) ?? "1", Margin = new Thickness(0, 3, 0, 3) }; NumericInputBehavior.SetSelectAllOnFocus(max, true); limits.Children.Add(max); Root.Children.Add(limits);
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
                var adjustmentPanel = new StackPanel { Margin = new Thickness(5, 0, 5, 0) }; adjustmentPanel.Children.Add(new TextBlock { Text = owner.Label("AdjustmentTtc", "TTC adjustment") }); adjustment = new TextBox { Width = 80, Text = draft?.PriceAdjustmentTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00" }; NumericInputBehavior.SetSelectAllOnFocus(adjustment, true); adjustmentPanel.Children.Add(adjustment);
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

    private sealed class AuthorityCloseChoiceDialog : Window
    {
        public AuthorityCloseChoiceDialog(
            Window owner,
            IReadOnlyDictionary<string, string> labels)
        {
            Owner = owner;
            Title = Read(labels, "AuthorityCloseTitle", "Close Sushi81 POS");
            Width = 560;
            Height = 220;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "AuthorityClosePrompt", "This computer is the current authority. Choose how to close."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var retain = new Button { Content = Read(labels, "AuthorityCloseRetain", "Close and retain authority"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            retain.Click += (_, _) => Complete(new MainWindowCloseRequest(MainWindowCloseIntent.Retain));
            var transfer = new Button { Content = Read(labels, "AuthorityTransferClose", "Transfer authority and close"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
            transfer.Click += (_, _) => Complete(new MainWindowCloseRequest(MainWindowCloseIntent.Transfer));
            var cancel = new Button { Content = Read(labels, "AuthorityCloseCancel", "Cancel"), Padding = new Thickness(10, 5, 10, 5), IsCancel = true };
            cancel.Click += (_, _) => Complete(new MainWindowCloseRequest(MainWindowCloseIntent.Cancel));
            buttons.Children.Add(retain); buttons.Children.Add(transfer); buttons.Children.Add(cancel); root.Children.Add(buttons);
            Content = root;
        }

        public MainWindowCloseRequest Choice { get; private set; } = new(MainWindowCloseIntent.Cancel);

        private void Complete(MainWindowCloseRequest choice)
        {
            Choice = choice;
            DialogResult = true;
            Close();
        }

        private static string Read(IReadOnlyDictionary<string, string> labels, string key, string fallback) => labels.TryGetValue(key, out var value) ? value : fallback;
    }

    private sealed class AuthorityTargetSelectionDialog : Window
    {
        private readonly ComboBox targetSelector;

        public AuthorityTargetSelectionDialog(
            Window owner,
            IReadOnlyDictionary<string, string> labels,
            IReadOnlyList<DeviceRegistrationArtifact> targets)
        {
            Owner = owner;
            Title = Read(labels, "AuthorityTargetTitle", "Choose transfer target");
            Width = 560;
            Height = 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "AuthorityTargetPrompt", "Choose the exact current-generation device that will receive authority."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            root.Children.Add(new TextBlock { Text = Read(labels, "AuthorityTargetLabel", "Transfer target"), FontWeight = FontWeights.SemiBold });
            targetSelector = new ComboBox
            {
                DisplayMemberPath = nameof(TargetChoice.Label),
                SelectedValuePath = nameof(TargetChoice.DeviceId),
                IsEnabled = targets.Count > 0,
                Margin = new Thickness(0, 4, 0, 14),
                MinWidth = 360
            };
            targetSelector.ItemsSource = targets.Select(target => new TargetChoice(target.DeviceId, $"{target.DisplayName} ({target.DeviceId.ToString("N")[..8]})")).ToArray();
            targetSelector.SelectedIndex = targets.Count > 0 ? 0 : -1;
            root.Children.Add(targetSelector);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var confirm = new Button { Content = Read(labels, "AuthorityTargetConfirm", "Transfer to this device"), Padding = new Thickness(10, 5, 10, 5), IsEnabled = targets.Count > 0 };
            confirm.Click += (_, _) => { if (targetSelector.SelectedValue is Guid) DialogResult = true; };
            var cancel = new Button { Content = Read(labels, "AuthorityCloseCancel", "Cancel"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(confirm); buttons.Children.Add(cancel); root.Children.Add(buttons);
            Content = root;
        }

        public Guid? TargetDeviceId => targetSelector.SelectedValue is Guid selected ? selected : null;

        private static string Read(IReadOnlyDictionary<string, string> labels, string key, string fallback) => labels.TryGetValue(key, out var value) ? value : fallback;

        private sealed record TargetChoice(Guid DeviceId, string Label);
    }

    private sealed class DeviceJoinDialog : Window
    {
        private readonly TextBox displayNameBox;

        public DeviceJoinDialog(Window owner, IReadOnlyDictionary<string, string> labels)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = 420;
            Title = LocalizedText(owner, "JoinExistingLineage", "Join existing Sushi81 system");

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "JoinPrompt", "Join the configured Sushi81 system as a read-only device."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "JoinDisplayName", "Device name"),
                FontWeight = FontWeights.SemiBold
            });
            displayNameBox = new TextBox { MinWidth = 340, Margin = new Thickness(0, 5, 0, 14) };
            root.Children.Add(displayNameBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var confirm = new Button { Content = Read(labels, "JoinConfirm", "Join read-only"), Padding = new Thickness(10, 4, 10, 4), IsDefault = true };
            confirm.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(displayNameBox.Text))
                {
                    displayNameBox.Focus();
                    return;
                }

                DialogResult = true;
            };
            var cancel = new Button { Content = Read(labels, "AuthorityCloseCancel", "Cancel"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(confirm);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        public string DisplayName => displayNameBox.Text.Trim();

        private static string Read(IReadOnlyDictionary<string, string> labels, string key, string fallback) => labels.TryGetValue(key, out var value) ? value : fallback;
    }

    private sealed class DisasterRecoveryDialog : Window
    {
        private readonly ComboBox candidateSelector;
        private readonly CheckBox quarantineCheckBox;
        private readonly Button confirmButton;

        public DisasterRecoveryDialog(
            Window owner,
            IReadOnlyDictionary<string, string> labels,
            IReadOnlyList<RecoveryCandidate> candidates,
            RecoveryCandidate? recommended)
        {
            Owner = owner;
            Title = Read(labels, "M07DisasterRecovery", "Disaster Recovery");
            Width = 700;
            Height = candidates.Count > 0 ? 500 : 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MinWidth = 560;

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "M07QuarantineWarning", "The old authority/target must be stopped and quarantined before recovery."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkRed,
                Margin = new Thickness(0, 0, 0, 10)
            });
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "M07CandidateDataLossWarning", "Disaster Recovery creates a new generation and may lose changes after the selected candidate."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            candidateSelector = new ComboBox { MinWidth = 620, IsEnabled = candidates.Count > 0, Margin = new Thickness(0, 0, 0, 12) };
            candidateSelector.ItemsSource = candidates.Select(candidate => new CandidateChoice(
                candidate.CandidateId,
                $"{TypeLabel(labels, candidate)} | {Read(labels, "M07CandidateRevision", "Revision")}: {candidate.BusinessRevision} | "
                + $"{Read(labels, "M07CandidateHandoffVersion", "Handoff")}: {candidate.HandoffVersion} | "
                + $"{Read(labels, "M07CandidateSource", "Source")}: {candidate.SourceDeviceId.ToString("N")[..8]} | "
                + $"{Read(labels, "M07CandidateTimestamp", "Timestamp")}: {candidate.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC")).ToArray();
            var recommendedId = recommended?.CandidateId;
            candidateSelector.SelectedItem = candidateSelector.Items.OfType<CandidateChoice>().FirstOrDefault(choice => choice.CandidateId == recommendedId)
                ?? candidateSelector.Items.OfType<CandidateChoice>().FirstOrDefault();
            root.Children.Add(candidateSelector);

            quarantineCheckBox = new CheckBox
            {
                Content = Read(labels, "M07QuarantineConfirm", "I confirm the old device is unavailable and quarantined."),
                IsThreeState = false,
                Margin = new Thickness(0, 4, 0, 14)
            };
            quarantineCheckBox.Checked += (_, _) => UpdateConfirmation();
            quarantineCheckBox.Unchecked += (_, _) => UpdateConfirmation();
            root.Children.Add(quarantineCheckBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            confirmButton = new Button
            {
                Content = Read(labels, "M07ConfirmRecovery", "Confirm Disaster Recovery"),
                Padding = new Thickness(10, 5, 10, 5),
                IsDefault = true,
                IsEnabled = false
            };
            confirmButton.Click += (_, _) =>
            {
                if (QuarantineConfirmed && (candidateSelector.SelectedItem is CandidateChoice || candidates.Count == 0))
                    DialogResult = true;
            };
            var cancel = new Button
            {
                Content = Read(labels, "AuthorityCloseCancel", "Cancel"),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true
            };
            buttons.Children.Add(confirmButton);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        }

        public string? SelectedCandidateId => (candidateSelector.SelectedItem as CandidateChoice)?.CandidateId;

        public bool QuarantineConfirmed => quarantineCheckBox.IsChecked == true;

        private void UpdateConfirmation() => confirmButton.IsEnabled = QuarantineConfirmed &&
            (candidateSelector.Items.Count == 0 || candidateSelector.SelectedItem is CandidateChoice);

        private static string TypeLabel(IReadOnlyDictionary<string, string> labels, RecoveryCandidate candidate) =>
            Read(labels, candidate.Type == RecoveryCandidateType.GitHubHandoff ? "M07CandidateTypeGitHub" : "M07CandidateTypeOneDrive", candidate.TypeName);

        private static string Read(IReadOnlyDictionary<string, string> labels, string key, string fallback) => labels.TryGetValue(key, out var value) ? value : fallback;

        private sealed record CandidateChoice(string CandidateId, string Label)
        {
            public override string ToString() => Label;
        }
    }

    private sealed class M07SetupDialog : Window
    {
        private readonly TextBox oneDriveRootBox;
        private readonly TextBox githubOwnerBox;
        private readonly TextBox githubRepositoryBox;
        private readonly TextBox githubReleaseTagBox;
        private readonly TextBox githubReleaseNameBox;
        private readonly TextBox githubCredentialTargetBox;
        private readonly IReadOnlyDictionary<string, string> labels;

        public M07SetupDialog(Window owner, IReadOnlyDictionary<string, string> labels, LocalConfiguration configuration)
        {
            Owner = owner;
            this.labels = labels;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = 620;
            MaxWidth = 760;
            Title = Read(labels, "M07SetupTitle", "M07 technical setup");

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "M07SetupPrompt", "Select the existing shared root and non-secret transport settings."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(new TextBlock { Text = Read(labels, "M07SetupOneDriveRoot", "Sushi81 shared OneDrive root"), FontWeight = FontWeights.SemiBold });
            var rootPanel = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
            var browse = new Button { Content = Read(labels, "M07SetupBrowse", "Browse…"), Padding = new Thickness(10, 4, 10, 4) };
            DockPanel.SetDock(browse, Dock.Right);
            browse.Click += (_, _) => BrowseForRoot();
            oneDriveRootBox = new TextBox { MinWidth = 480, Text = configuration.OneDriveRoot ?? string.Empty, Margin = new Thickness(0, 0, 8, 0) };
            rootPanel.Children.Add(browse);
            rootPanel.Children.Add(oneDriveRootBox);
            root.Children.Add(rootPanel);

            githubOwnerBox = AddField(root, "M07SetupGitHubOwner", configuration.GitHubOwner);
            githubRepositoryBox = AddField(root, "M07SetupGitHubRepository", configuration.GitHubRepository);
            githubReleaseTagBox = AddField(root, "M07SetupGitHubReleaseTag", configuration.GitHubReleaseTag);
            githubReleaseNameBox = AddField(root, "M07SetupGitHubReleaseName", configuration.GitHubReleaseName);
            githubCredentialTargetBox = AddField(root, "M07SetupGitHubCredentialTarget", configuration.GitHubCredentialTarget);
            root.Children.Add(new TextBlock
            {
                Text = Read(labels, "M07SetupGitHubHelp", "Enter only the protected credential target name. Never enter a PAT."),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72,
                Margin = new Thickness(0, -2, 0, 14)
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = Read(labels, "M07SetupSave", "Validate and require restart"), Padding = new Thickness(10, 4, 10, 4), IsDefault = true };
            save.Click += (_, _) => DialogResult = true;
            var cancel = new Button { Content = Read(labels, "AuthorityCloseCancel", "Cancel"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        public M07ConfigurationSetupInput Input => new(
            oneDriveRootBox.Text,
            githubOwnerBox.Text,
            githubRepositoryBox.Text,
            githubReleaseTagBox.Text,
            githubReleaseNameBox.Text,
            githubCredentialTargetBox.Text);

        private TextBox AddField(Panel parent, string labelKey, string? value)
        {
            parent.Children.Add(new TextBlock { Text = Read(labels, labelKey, labelKey), FontWeight = FontWeights.SemiBold });
            var box = new TextBox { Text = value ?? string.Empty, Margin = new Thickness(0, 4, 0, 10), MinWidth = 480 };
            parent.Children.Add(box);
            return box;
        }

        private void BrowseForRoot()
        {
            var picker = new OpenFolderDialog
            {
                Multiselect = false,
                FolderName = Directory.Exists(oneDriveRootBox.Text) ? oneDriveRootBox.Text : string.Empty
            };
            if (picker.ShowDialog() == true)
                oneDriveRootBox.Text = picker.FolderName;
        }

        private static string Read(IReadOnlyDictionary<string, string> labels, string key, string fallback) => labels.TryGetValue(key, out var value) ? value : fallback;
    }
}
