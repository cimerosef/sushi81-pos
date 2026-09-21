using Sushi81.Pos.Infrastructure.Paths;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M12ArchivePathTests
{
    [TestMethod]
    public void WindowsAppPathsCreatesArchiveDirectoryDistinctFromTempAndCache()
    {
        var paths = new WindowsAppPaths();

        paths.EnsureInitialized();

        var expectedArchiveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sushi81 POS",
            "Archive");

        Assert.AreEqual(expectedArchiveDirectory, paths.ArchiveDirectory);
        Assert.IsTrue(Directory.Exists(paths.ArchiveDirectory));
        Assert.AreNotEqual(paths.CacheDirectory, paths.ArchiveDirectory);
        Assert.AreNotEqual(paths.TempDirectory, paths.ArchiveDirectory);
    }
}
