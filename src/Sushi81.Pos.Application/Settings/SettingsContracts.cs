using Sushi81.Pos.Domain;
using Sushi81.Pos.Application.Catalogue;

namespace Sushi81.Pos.Application.Settings;

public interface IBusinessSettingsStore
{
    Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default);
}

public sealed class BusinessSettingsService(IBusinessSettingsStore store)
{
    private readonly IBusinessSettingsStore store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => store.GetAsync(cancellationToken);

    public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default)
    {
        var error = CatalogueValidation.ValidateSettings(settings);
        return error is null ? store.UpdateAsync(settings, cancellationToken) : Task.FromResult(OperationResult.Failure(new ValidationIssue("settings", error)));
    }
}
