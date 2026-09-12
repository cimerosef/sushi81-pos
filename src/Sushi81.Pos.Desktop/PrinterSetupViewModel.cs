using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Printing;

namespace Sushi81.Pos.Desktop;

/// <summary>Local technical printer selection. It is intentionally independent from business authority.</summary>
public sealed class PrinterSetupViewModel : INotifyPropertyChanged
{
    private readonly ILocalConfigurationService configurationService;
    private readonly IPrintQueueCatalog queueCatalog;
    private LocalConfiguration configuration;
    private string kitchenQueueId = string.Empty;
    private string kitchenQueueName = string.Empty;
    private string customerQueueId = string.Empty;
    private string customerQueueName = string.Empty;
    private string statusMessage = string.Empty;
    private bool isBusy;
    private IReadOnlyDictionary<string, string> localized = new Dictionary<string, string>(StringComparer.Ordinal);

    public PrinterSetupViewModel(
        LocalConfiguration configuration,
        ILocalConfigurationService configurationService,
        IPrintQueueCatalog queueCatalog)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        this.queueCatalog = queueCatalog ?? throw new ArgumentNullException(nameof(queueCatalog));
        kitchenQueueId = configuration.KitchenPrinterQueueId ?? string.Empty;
        kitchenQueueName = configuration.KitchenPrinterQueueName ?? string.Empty;
        customerQueueId = configuration.CustomerPrinterQueueId ?? string.Empty;
        customerQueueName = configuration.CustomerPrinterQueueName ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PrintQueueInfo> Queues { get; } = [];

    public string KitchenQueueId
    {
        get => kitchenQueueId;
        set
        {
            kitchenQueueId = value ?? string.Empty;
            if (Queues.FirstOrDefault(queue => string.Equals(queue.Id, kitchenQueueId, StringComparison.OrdinalIgnoreCase)) is { } queue)
            {
                kitchenQueueName = queue.Name;
                OnPropertyChanged(nameof(KitchenQueueName));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public string KitchenQueueName
    {
        get => kitchenQueueName;
        set { kitchenQueueName = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    public string CustomerQueueId
    {
        get => customerQueueId;
        set
        {
            customerQueueId = value ?? string.Empty;
            if (Queues.FirstOrDefault(queue => string.Equals(queue.Id, customerQueueId, StringComparison.OrdinalIgnoreCase)) is { } queue)
            {
                customerQueueName = queue.Name;
                OnPropertyChanged(nameof(CustomerQueueName));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    public string CustomerQueueName
    {
        get => customerQueueName;
        set { customerQueueName = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set { statusMessage = value ?? string.Empty; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSave)); }
    }

    public bool CanSave => !IsBusy;

    public void ApplyLocalization(IReadOnlyDictionary<string, string> values)
    {
        localized = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var queues = await queueCatalog.ListAsync(cancellationToken);
            Queues.Clear();
            foreach (var queue in queues) Queues.Add(queue);
            ApplyQueueNamesFromSelections();
            StatusMessage = HasUnavailableConfiguredQueue()
                ? Text("PrinterQueueUnavailable", "A configured printer queue is unavailable. Reselect it before printing.")
                : Text("PrinterRefreshSucceeded", "Installed printer queues refreshed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            StatusMessage = Text("PrinterRefreshFailed", "Installed printer queues could not be listed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            configuration = await configurationService.UpdateAsync(
                current => current with
                {
                    KitchenPrinterQueueId = NullIfBlank(KitchenQueueId),
                    KitchenPrinterQueueName = NullIfBlank(KitchenQueueName),
                    CustomerPrinterQueueId = NullIfBlank(CustomerQueueId),
                    CustomerPrinterQueueName = NullIfBlank(CustomerQueueName)
                },
                cancellationToken);
            StatusMessage = Text("PrinterSaved", "Printer selections saved locally.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            StatusMessage = Text("PrinterSaveFailed", "Printer selections could not be saved.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyQueueNamesFromSelections()
    {
        if (string.IsNullOrWhiteSpace(KitchenQueueId) && !string.IsNullOrWhiteSpace(KitchenQueueName))
        {
            var configuredKitchen = Queues.FirstOrDefault(queue => string.Equals(queue.Name, KitchenQueueName, StringComparison.OrdinalIgnoreCase));
            if (configuredKitchen is not null) KitchenQueueId = configuredKitchen.Id;
        }
        var kitchen = Queues.FirstOrDefault(queue => string.Equals(queue.Id, KitchenQueueId, StringComparison.OrdinalIgnoreCase));
        if (kitchen is not null && string.IsNullOrWhiteSpace(KitchenQueueName)) KitchenQueueName = kitchen.Name;
        if (string.IsNullOrWhiteSpace(CustomerQueueId) && !string.IsNullOrWhiteSpace(CustomerQueueName))
        {
            var configuredCustomer = Queues.FirstOrDefault(queue => string.Equals(queue.Name, CustomerQueueName, StringComparison.OrdinalIgnoreCase));
            if (configuredCustomer is not null) CustomerQueueId = configuredCustomer.Id;
        }
        var customer = Queues.FirstOrDefault(queue => string.Equals(queue.Id, CustomerQueueId, StringComparison.OrdinalIgnoreCase));
        if (customer is not null && string.IsNullOrWhiteSpace(CustomerQueueName)) CustomerQueueName = customer.Name;
    }

    private bool HasUnavailableConfiguredQueue() =>
        IsUnavailable(KitchenQueueId, KitchenQueueName) || IsUnavailable(CustomerQueueId, CustomerQueueName);

    private bool IsUnavailable(string id, string name) =>
        (!string.IsNullOrWhiteSpace(id) || !string.IsNullOrWhiteSpace(name))
        && !Queues.Any(queue =>
            (!string.IsNullOrWhiteSpace(id) && string.Equals(queue.Id, id, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(name) && string.Equals(queue.Name, name, StringComparison.OrdinalIgnoreCase)));

    private string Text(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
