namespace Sushi81.Pos.Application.Foundation.Configuration;

public interface ILocalConfigurationService
{
    Task<LocalConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LocalConfiguration configuration, CancellationToken cancellationToken = default);
}
