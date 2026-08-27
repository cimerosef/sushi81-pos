using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Configuration;

public sealed class JsonLocalConfigurationService(IAppPaths paths) : ILocalConfigurationService
{
    private const string ConfigurationFileName = "local-settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<LocalConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var configurationPath = Path.Combine(paths.ConfigDirectory, ConfigurationFileName);
        if (!File.Exists(configurationPath))
        {
            return new LocalConfiguration();
        }

        try
        {
            await using var stream = new FileStream(
                configurationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var configuration = await JsonSerializer.DeserializeAsync<LocalConfiguration>(stream, SerializerOptions, cancellationToken);
            return configuration ?? throw new InvalidDataException("The local configuration file contains no configuration object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The local configuration file '{configurationPath}' is malformed. Correct or replace it before starting Sushi81 POS.", exception);
        }
    }

    public async Task SaveAsync(LocalConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        paths.EnsureInitialized();
        var configurationPath = Path.Combine(paths.ConfigDirectory, ConfigurationFileName);
        var temporaryPath = Path.Combine(paths.ConfigDirectory, $".{ConfigurationFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, configurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
