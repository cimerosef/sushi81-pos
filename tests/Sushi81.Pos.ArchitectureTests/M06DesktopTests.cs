using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M06DesktopTests
{
    [TestMethod]
    public void RealShellShowsStaleReadOnlySafetyBoundaryAcrossStatesAndSupportedSizesOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var state in new[]
            {
                WriteAuthorityState.Authoritative,
                WriteAuthorityState.NonAuthoritativeReadOnly,
                WriteAuthorityState.Transitioning,
                WriteAuthorityState.RecoveryRequired
            })
            {
                var guard = new TestGuard(state);
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(),
                    true,
                    new CatalogueService(new EmptyCatalogueStore()),
                    new BusinessSettingsService(new EmptySettingsStore()),
                    authorityGuard: guard,
                    authorityState: state);

                foreach (var size in new[] { (760d, 520d), (980d, 680d), (1400d, 900d) })
                {
                    var window = new MainWindow(shell)
                    {
                        Width = size.Item1,
                        Height = size.Item2,
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = 0,
                        Top = 0
                    };
                    window.Show();
                    try
                    {
                        window.UpdateLayout();
                        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                        window.UpdateLayout();

                        var banner = VisualDescendants<TextBlock>(window).Single(text => text.Text == shell.AuthorityStatus && text.Visibility == Visibility.Visible);
                        Assert.IsGreaterThan(0d, banner.ActualWidth, $"{state} banner width at {size.Item1}x{size.Item2}");
                        Assert.IsGreaterThan(0d, banner.ActualHeight, $"{state} banner height at {size.Item1}x{size.Item2}");
                        Assert.IsLessThanOrEqualTo(window.ActualWidth, banner.ActualWidth, "The safety banner must remain hosted by the window.");

                        var search = VisualDescendants<TextBox>(window).FirstOrDefault(textBox => textBox.IsEnabled);
                        Assert.IsNotNull(search, $"{state} must retain a usable read-only consultation/search route.");
                        var create = VisualDescendants<Button>(window).Single(button => Equals(button.Content, shell.Localized["NewProduct"]));

                        if (state == WriteAuthorityState.Authoritative)
                        {
                            Assert.IsTrue(shell.CanWrite);
                            Assert.IsTrue(shell.Admin!.CanCreateProduct);
                            Assert.IsTrue(create.IsEnabled);
                        }
                        else
                        {
                            Assert.IsFalse(shell.CanWrite);
                            Assert.IsFalse(shell.Admin!.CanCreateProduct);
                            Assert.IsFalse(create.IsEnabled);
                            Assert.IsTrue(shell.AuthorityStatus.Contains("obsolètes", StringComparison.Ordinal));
                        }
                    }
                    finally
                    {
                        window.Close();
                    }
                }

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                Assert.IsTrue(shell.AuthorityStatus.Contains("过时", StringComparison.Ordinal));
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.IsTrue(shell.AuthorityStatus.Contains("obsolètes", StringComparison.Ordinal));
            }
        });
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class TestGuard(WriteAuthorityState state) : IWriteAuthorityGuard
    {
        public WriteAuthorityState State { get; } = state;
        public void RequireWriteAuthority()
        {
            if (State != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(State);
        }
    }

    private sealed class EmptyCatalogueStore : ICatalogueStore
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name, shortCode)));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name, shortCode)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class EmptySettingsStore : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }
}
