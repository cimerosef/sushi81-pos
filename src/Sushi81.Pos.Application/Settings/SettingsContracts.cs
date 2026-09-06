using Sushi81.Pos.Domain;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;

namespace Sushi81.Pos.Application.Settings;

public interface IBusinessSettingsStore
{
    Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default);
}

public sealed class BusinessSettingsService
{
    private readonly IBusinessSettingsStore store;
    private readonly IWriteAuthorityGuard authorityGuard;
    private readonly IDurableChangeNotifier notifier;

    public BusinessSettingsService(IBusinessSettingsStore store, IWriteAuthorityGuard authorityGuard, IDurableChangeNotifier notifier)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
        this.notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    // Test assemblies use the explicit test-only wiring supplied by the application project.
    internal BusinessSettingsService(IBusinessSettingsStore store)
        : this(store, TestOnlyAuthoritativeGuard.Instance, TestOnlyDurableChangeNotifier.Instance) { }

    public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => store.GetAsync(cancellationToken);

    public async Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default)
    {
        var error = CatalogueValidation.ValidateSettings(settings);
        if (error is not null) return OperationResult.Failure(new ValidationIssue("settings", error));

        try
        {
            authorityGuard.RequireWriteAuthority();
            var current = await store.GetAsync(cancellationToken);
            var hasEffectiveChange = current.PickupDiscountRate != settings.PickupDiscountRate
                || current.PickupDiscountMinTotalTtc != settings.PickupDiscountMinTotalTtc
                || current.DeliveryMinMerchandiseTotalTtc != settings.DeliveryMinMerchandiseTotalTtc
                || current.DeliveryFeeEnabled != settings.DeliveryFeeEnabled
                || current.DeliveryFeeAmountTtc != settings.DeliveryFeeAmountTtc;
            var result = await store.UpdateAsync(settings, cancellationToken);
            if (result.Succeeded && hasEffectiveChange)
            {
                try { await notifier.NotifyCommittedAsync(CancellationToken.None); }
                catch { /* committed business data remains authoritative */ }
            }
            return result;
        }
        catch (WriteAuthorityException exception)
        {
            return OperationResult.Failure(new ValidationIssue("authority", $"Local write authority is unavailable ({exception.State}).", ValidationCodes.AuthorityBlocked));
        }
    }
}
