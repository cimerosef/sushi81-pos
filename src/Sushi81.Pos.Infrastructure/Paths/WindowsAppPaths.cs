using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Paths;

/// <summary>Production paths rooted in the non-disposable local application-data area.</summary>
public sealed class WindowsAppPaths : IAppPaths
{
    public WindowsAppPaths()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sushi81 POS");
        DataDirectory = Path.Combine(RootDirectory, "Data");
        RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
        CacheDirectory = Path.Combine(RootDirectory, "Cache");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        ConfigDirectory = Path.Combine(RootDirectory, "Config");
        TempDirectory = Path.Combine(RootDirectory, "Temp");
        ArchiveDirectory = Path.Combine(RootDirectory, "Archive");
        LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
    }

    public string RootDirectory { get; }

    public string DataDirectory { get; }

    public string RecoveryDirectory { get; }

    public string CacheDirectory { get; }

    public string LogsDirectory { get; }

    public string ConfigDirectory { get; }

    public string TempDirectory { get; }

    public string ArchiveDirectory { get; }

    public string LiveDatabasePath { get; }

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(ArchiveDirectory);
    }
}
