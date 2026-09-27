namespace Sushi81.Pos.Application.Foundation.Paths;

using Sushi81.Pos.Application.Foundation;

public interface IAppPaths
{
    DeploymentProfile Profile => DeploymentProfile.Production;

    string RootDirectory { get; }

    string DataDirectory { get; }

    string RecoveryDirectory { get; }

    string CacheDirectory { get; }

    string LogsDirectory { get; }

    string ConfigDirectory { get; }

    string TempDirectory { get; }

    string ArchiveDirectory => Path.Combine(RootDirectory, "Archive");

    string LiveDatabasePath { get; }

    void EnsureInitialized();
}
