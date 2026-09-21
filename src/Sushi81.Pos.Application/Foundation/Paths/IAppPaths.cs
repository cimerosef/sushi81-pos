namespace Sushi81.Pos.Application.Foundation.Paths;

public interface IAppPaths
{
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
