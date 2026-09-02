using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Domain.Tests;

[TestClass]
public sealed class CatalogueDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CategoryNormalizationTrimsComposesAndUppercasesInvariantly()
    {
        Assert.AreEqual("CAFÉ", CatalogueNormalization.Key("  cafe\u0301 "));
        Assert.IsTrue(Category.TryCreate(Guid.NewGuid(), "  Plats  ", Now, out var category, out _));
        Assert.AreEqual("Plats", category.Name);
    }

    [TestMethod]
    public void CategoryShortCodeIsIndependentAndLengthBounded()
    {
        var category = new Category(Guid.NewGuid(), "Plats", Now, Now) { ShortCode = " PL " };

        Assert.AreEqual(" PL ", category.ShortCode);
        Assert.AreEqual("PL", category.NormalizedShortCode);
        Assert.IsNull(CatalogueValidation.ValidateCategoryShortCode("123456789012"));
        Assert.AreEqual("Category short code cannot exceed 12 characters.", CatalogueValidation.ValidateCategoryShortCode("1234567890123"));
    }

    [TestMethod]
    public void ProductValidationCoversRequiredPriceAndVatBoundaries()
    {
        Assert.AreEqual("Product code is required.", CatalogueValidation.ValidateProduct(" ", "Sushi", Guid.NewGuid(), Money.Zero, 10m));
        Assert.AreEqual("Product name is required.", CatalogueValidation.ValidateProduct("A", " ", Guid.NewGuid(), Money.Zero, 10m));
        Assert.AreEqual("Product price cannot be negative.", CatalogueValidation.ValidateProduct("A", "Sushi", Guid.NewGuid(), Money.FromCents(-1), 10m));
        Assert.AreEqual("VAT rate must be between 0 and 100 percent.", CatalogueValidation.ValidateProduct("A", "Sushi", Guid.NewGuid(), Money.Zero, 100.1m));
        Assert.IsNull(CatalogueValidation.ValidateProduct("A", "Sushi", Guid.NewGuid(), Money.Zero, 0m));
        Assert.IsNull(CatalogueValidation.ValidateProduct("A", "Sushi", Guid.NewGuid(), Money.Zero, 100m));
    }

    [TestMethod]
    public void OptionGroupsEnforceSingleAndMultiStructure()
    {
        var single = new OptionGroup(Guid.NewGuid(), Guid.NewGuid(), "Size", SelectionMode.Single, true, null, null, 0, Now, Now);
        Assert.IsNull(CatalogueValidation.ValidateGroup(single));
        Assert.IsNotNull(CatalogueValidation.ValidateGroup(single with { MinSelections = 1 }));
        Assert.IsNull(CatalogueValidation.ValidateGroup(single with { IsRequired = false }));
        var multi = single with { SelectionMode = SelectionMode.Multi, MinSelections = 1, MaxSelections = 2 };
        Assert.IsNull(CatalogueValidation.ValidateGroup(multi));
        Assert.IsNotNull(CatalogueValidation.ValidateGroup(multi with { MinSelections = 0, IsRequired = true }));
        Assert.IsNotNull(CatalogueValidation.ValidateGroup(multi with { MinSelections = 3 }));
    }

    [TestMethod]
    public void OptionAdjustmentsAllowSignedAndZeroMoney()
    {
        var group = Guid.NewGuid();
        Assert.IsNull(CatalogueValidation.ValidateOption(new(Guid.NewGuid(), group, "Plus", Money.FromCents(25), true, 0, Now, Now)));
        Assert.IsNull(CatalogueValidation.ValidateOption(new(Guid.NewGuid(), group, "Minus", Money.FromCents(-25), true, 1, Now, Now)));
        Assert.IsNull(CatalogueValidation.ValidateOption(new(Guid.NewGuid(), group, "Zero", Money.Zero, true, 2, Now, Now)));
    }

    [TestMethod]
    public void MultiRequiredAndOptionalBoundariesAreExplicit()
    {
        var baseGroup = new OptionGroup(Guid.NewGuid(), Guid.NewGuid(), "Extras", SelectionMode.Multi, false, 0, 1, 0, Now, Now);
        Assert.IsNull(CatalogueValidation.ValidateGroup(baseGroup));
        Assert.IsNull(CatalogueValidation.ValidateGroup(baseGroup with { IsRequired = true, MinSelections = 1 }));
        Assert.IsNotNull(CatalogueValidation.ValidateGroup(baseGroup with { MinSelections = -1 }));
        Assert.IsNotNull(CatalogueValidation.ValidateGroup(baseGroup with { MaxSelections = 0 }));
        Assert.IsNotNull(CatalogueValidation.ValidateGroup(baseGroup with { MinSelections = 2, MaxSelections = 1 }));
    }

    [TestMethod]
    public void RequiredEnabledGroupsNeedEnoughActiveChoices()
    {
        var product = new Product(Guid.NewGuid(), "A", "Sushi", Guid.NewGuid(), Money.Zero, 10m, true, true, true, Now, Now);
        var group = new OptionGroup(Guid.NewGuid(), product.Id, "Size", SelectionMode.Single, true, null, null, 0, Now, Now);
        var aggregate = new ProductAggregate(product, [group], new Dictionary<Guid, IReadOnlyList<ProductOption>> { [group.Id] = [] });
        Assert.IsNotNull(CatalogueValidation.ValidateRequiredChoices(aggregate));
        aggregate = aggregate with { OptionsByGroup = new Dictionary<Guid, IReadOnlyList<ProductOption>> { [group.Id] = [new(Guid.NewGuid(), group.Id, "Large", Money.Zero, true, 0, Now, Now)] } };
        Assert.IsNull(CatalogueValidation.ValidateRequiredChoices(aggregate));
    }

    [TestMethod]
    public void SettingsValidationAndDefaultsUseDecimalFractionAndMoney()
    {
        var defaults = BusinessSettings.Defaults(Now);
        Assert.AreEqual(0.10m, defaults.PickupDiscountRate);
        Assert.AreEqual(1500L, defaults.PickupDiscountMinTotalTtc.Cents);
        Assert.IsNull(CatalogueValidation.ValidateSettings(defaults));
        Assert.IsNotNull(CatalogueValidation.ValidateSettings(defaults with { PickupDiscountRate = 1.01m }));
        Assert.IsNotNull(CatalogueValidation.ValidateSettings(defaults with { DeliveryFeeAmountTtc = Money.FromCents(-1) }));
        Assert.IsNull(CatalogueValidation.ValidateSettings(defaults with { PickupDiscountRate = 0m }));
        Assert.IsNull(CatalogueValidation.ValidateSettings(defaults with { PickupDiscountRate = 1m }));
        Assert.IsNotNull(CatalogueValidation.ValidateSettings(defaults with { PickupDiscountRate = -0.01m }));
    }
}
