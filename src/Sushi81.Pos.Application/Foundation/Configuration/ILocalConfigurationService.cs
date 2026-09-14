namespace Sushi81.Pos.Application.Foundation.Configuration;

public interface ILocalConfigurationService
{
    Task<LocalConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LocalConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically applies a local technical-configuration update to the latest persisted
    /// snapshot. Callers must mutate only the fields they own so concurrent writers cannot
    /// overwrite unrelated language, printer or M07 settings.
    /// </summary>
    Task<LocalConfiguration> UpdateAsync(
        Func<LocalConfiguration, LocalConfiguration> update,
        CancellationToken cancellationToken = default);
}
