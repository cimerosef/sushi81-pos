using System.Globalization;
using Sushi81.Pos.Desktop;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class LocalizationTests
{
    [TestMethod]
    public void ShellUsesFrenchByDefaultAndPersistsAChineseSwitch()
    {
        var store = new InMemorySelectedCultureStore();
        var viewModel = new ShellViewModel(store, startupSucceeded: true);

        Assert.AreEqual("Fondation prête.", viewModel.Status);
        Assert.AreEqual("Langue", viewModel.LanguageLabel);

        viewModel.SelectedLanguage = viewModel.Languages.Single(option => option.CultureName == "zh-CN");

        Assert.AreEqual("基础已就绪。", viewModel.Status);
        Assert.AreEqual("语言", viewModel.LanguageLabel);
        Assert.AreEqual("zh-CN", store.Load().Name);
    }

    [TestMethod]
    public void UnsupportedPersistedCultureFallsBackToFrench()
    {
        var store = new FixedCultureStore(CultureInfo.GetCultureInfo("en-US"));
        var viewModel = new ShellViewModel(store, startupSucceeded: false);

        Assert.AreEqual("Le démarrage a échoué. Consultez les diagnostics.", viewModel.Status);
    }

    private sealed class FixedCultureStore(CultureInfo culture) : ISelectedCultureStore
    {
        public CultureInfo Load() => culture;

        public void Save(CultureInfo selectedCulture) => throw new AssertFailedException("The test does not switch culture.");
    }
}
