using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Printing;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M08PrintingIntegrationTests
{
    [TestMethod]
    public async Task ReceiptIdentityMigrationSeedsAndPersistsTheApprovedBusinessValues()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();

        await using (var connection = await factory.OpenLiveConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT receipt_business_name, receipt_address_line_1, receipt_address_line_2, receipt_siret, receipt_vat_number, receipt_activity_code FROM business_settings;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(ReceiptIdentity.Default.BusinessName, reader.GetString(0));
            Assert.AreEqual(ReceiptIdentity.Default.AddressLine1, reader.GetString(1));
            Assert.AreEqual(ReceiptIdentity.Default.AddressLine2, reader.GetString(2));
            Assert.AreEqual(ReceiptIdentity.Default.Siret, reader.GetString(3));
            Assert.AreEqual(ReceiptIdentity.Default.VatNumber, reader.GetString(4));
            Assert.AreEqual(ReceiptIdentity.Default.ActivityCode, reader.GetString(5));
        }

        var store = new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock);
        var updatedIdentity = new ReceiptIdentity("Sushi 81 Test", "Adresse 1", "Adresse 2", "SIRET", "TVA", "APE");
        var updated = (await store.GetAsync()) with { ReceiptIdentity = updatedIdentity };
        Assert.IsTrue((await store.UpdateAsync(updated)).Succeeded);
        var reopened = await store.GetAsync();
        Assert.AreEqual(updatedIdentity, reopened.ReceiptIdentity);
    }

    [TestMethod]
    public async Task ThermalLayoutPaginatesToTheQueueImageableHeightOnAnStaThread()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var surface = new PrintImageableSurface(220, 90, 5, 5, 210, 30);
                var pages = ThermalPrintLayout.Paginate(string.Join(Environment.NewLine, Enumerable.Repeat("Ligne de test longue pour la pagination", 12)), surface);
                completion.TrySetResult(pages);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var pages = await completion.Task;
        Assert.IsGreaterThan(1, pages.Count);
        Assert.IsTrue(pages.All(page => !string.IsNullOrWhiteSpace(page)));
    }

    [TestMethod]
    public async Task SemanticReceiptRendererOwnsAlignmentAndKeepsAtomicHeaderTogether()
    {
        var content = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***", AtomicGroup: "header"),
            new(PrintReceiptBlockKind.LabelValue, "Cmd", "20260912-001", AtomicGroup: "header"),
            new(PrintReceiptBlockKind.LabelValue, "Heure", "10:15", AtomicGroup: "header"),
            new(PrintReceiptBlockKind.Separator, "-"),
            new(PrintReceiptBlockKind.LabelValue, "Adresse", "12 rue très longue à Nemours qui doit être enroulée sans perdre de texte"),
            new(PrintReceiptBlockKind.Total, "TOTAL", "12.50 EUR", AtomicGroup: "total")
        ]);

        var pages = await StaPrintThread.RunAsync(
            () => ThermalPrintLayout.Paginate(content, new PrintImageableSurface(220, 150, 5, 5, 210, 90)),
            CancellationToken.None);

        Assert.IsGreaterThan(1, pages.Count);
        StringAssert.Contains(pages[0], "*** CUISINE ***");
        StringAssert.Contains(pages[0], "Cmd : 20260912-001");
        StringAssert.Contains(pages[0], "Heure : 10:15");
        Assert.IsTrue(pages.SelectMany(page => page.Split(Environment.NewLine)).Any(line => line.Contains("TOTAL : 12.50 EUR", StringComparison.Ordinal)));
        var flattenedAddress = string.Join(" ", pages.SelectMany(page => page.Split(Environment.NewLine)));
        StringAssert.Contains(flattenedAddress, "12 rue très");
        StringAssert.Contains(flattenedAddress, "longue à");
        StringAssert.Contains(flattenedAddress, "Nemours");
        Assert.IsFalse(pages.Any(page => page.Contains("Catalogue actuel", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SemanticReceiptRendererUsesNarrowImageableWidthWithoutFixedWidthFragments()
    {
        var content = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Identity, "Sushi 81", AtomicGroup: "identity"),
            new(PrintReceiptBlockKind.Identity, "SIRET", "90805211100014", AtomicGroup: "identity"),
            new(PrintReceiptBlockKind.Item, "1x P-001", "Un nom de produit volontairement très long pour tester le rendu thermique")
        ]);

        var pages = await StaPrintThread.RunAsync(
            () => ThermalPrintLayout.Paginate(content, new PrintImageableSurface(220, 240, 2, 2, 210, 180)),
            CancellationToken.None);

        var rendered = string.Join(Environment.NewLine, pages);
        StringAssert.Contains(rendered, "Sushi 81");
        StringAssert.Contains(rendered, "908052111000");
        StringAssert.Contains(rendered, "14");
        StringAssert.Contains(rendered, "nom de");
        StringAssert.Contains(rendered, "volontaireme");
        Assert.IsFalse(rendered.Contains(new string(' ', 20), StringComparison.Ordinal));
    }

    [TestMethod]
    public void ThermalContentWidthCapsWideVirtualQueuesButNeverWidensNarrowQueues()
    {
        var wide = new PrintImageableSurface(1000, 2400, 5, 5, 800, 2390);
        var narrow = new PrintImageableSurface(220, 2400, 5, 5, 210, 2390);

        Assert.AreEqual(
            ThermalPrintLayout.ThermalPrintableWidth72Mm - ThermalPrintLayout.ThermalSafeInsetLeft - ThermalPrintLayout.ThermalSafeInsetRight,
            ThermalPrintLayout.EffectiveContentWidth(wide),
            0.01);
        Assert.AreEqual(
            210d - ThermalPrintLayout.ThermalSafeInsetLeft - ThermalPrintLayout.ThermalSafeInsetRight,
            ThermalPrintLayout.EffectiveContentWidth(narrow),
            0.01);
        Assert.AreEqual(
            wide.OriginWidth + (wide.ImageableWidth - ThermalPrintLayout.ThermalPrintableWidth72Mm) / 2 + ThermalPrintLayout.ThermalSafeInsetLeft,
            ThermalPrintLayout.EffectiveContentOriginWidth(wide),
            0.01);
        Assert.AreEqual(narrow.OriginWidth + ThermalPrintLayout.ThermalSafeInsetLeft, ThermalPrintLayout.EffectiveContentOriginWidth(narrow), 0.01);
    }

    [TestMethod]
    public async Task StructuredRendererExpressesR07HierarchyIndentationAndCustomerRows()
    {
        var kitchenContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***", AtomicGroup: "kitchen-header"),
            new(PrintReceiptBlockKind.Item, "1x P-001", "Plat") { },
            new(PrintReceiptBlockKind.Option, "Option", "2.50 EUR")
        ]);
        var customerContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.BusinessName, "Sushi 81", AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.LegalIdentity, "90805211100014 FR03908052111 5610C", AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.Ticket, "20260913-001", "13/09/2026 12:30", AtomicGroup: "customer-ticket"),
            new(PrintReceiptBlockKind.CustomerInfo, "Adresse", "12 rue de Nemours", AtomicGroup: "customer-info"),
            new(PrintReceiptBlockKind.Item, "2 x P-002", "Plat deux")
            {
                Item = new("2x", "P-002 Plat deux", "8.00 EUR", "16.00 EUR")
            },
            new(PrintReceiptBlockKind.Tax, "Total HT", "14.55 EUR", AtomicGroup: "customer-tax"),
            new(PrintReceiptBlockKind.Total, "Total EUR", "16.00", AtomicGroup: "customer-total"),
            new(PrintReceiptBlockKind.Footer, "Merci de votre visite !", AtomicGroup: "customer-footer")
        ]);

        var (kitchenBlocks, customerBlocks, itemIsGrid) = await StaPrintThread.RunAsync(
            () =>
            {
                var surface = new PrintImageableSurface(400, 300, 5, 5, 390, 280);
                return (
                    ThermalPrintLayout.RenderPages(kitchenContent, surface).Single(),
                    ThermalPrintLayout.RenderPages(customerContent, surface).Single(),
                    ThermalPrintLayout.CreateVisual(
                        ThermalPrintLayout.RenderPages(customerContent, surface).Single().Single(block => block.Item is not null),
                        300) is Grid);
            },
            CancellationToken.None);

        var heading = kitchenBlocks.Single(block => block.Text == "*** CUISINE ***");
        var option = kitchenBlocks.Single(block => block.Text.Contains("Option", StringComparison.Ordinal));
        var businessName = customerBlocks.Single(block => block.Text == "Sushi 81");
        var legal = customerBlocks.Single(block => block.Text == "90805211100014 FR03908052111 5610C");
        var ticket = customerBlocks.Single(block => block.Text.Contains("20260913-001", StringComparison.Ordinal));
        var item = customerBlocks.Single(block => block.Item is not null);
        var total = customerBlocks.Single(block => block.Text == "Total EUR 16.00");

        Assert.AreEqual(ThermalPrintLayout.KitchenHeadingFontSize, heading.FontSize);
        Assert.AreEqual(TextAlignment.Center, heading.Alignment);
        Assert.AreEqual(ThermalPrintLayout.CustomerBusinessNameFontSize, businessName.FontSize);
        Assert.AreEqual(TextAlignment.Center, businessName.Alignment);
        Assert.AreEqual(TextAlignment.Center, legal.Alignment);
        Assert.AreEqual(TextAlignment.Left, ticket.Alignment);
        Assert.IsTrue(option.IsIndented);
        Assert.IsNotNull(item.Item);
        Assert.IsTrue(itemIsGrid);
        Assert.AreEqual(ThermalPrintLayout.CustomerTotalFontSize, total.FontSize);
        Assert.AreEqual(FontWeights.Bold, total.FontWeight);
        StringAssert.Contains(string.Join(Environment.NewLine, customerBlocks.Select(block => block.Text)), "Merci de votre visite !");
    }

    [TestMethod]
    public async Task StructuredRendererExpressesR08CustomerSpacingTrimmedRowsTotalAndFooter()
    {
        var customerContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.BusinessName, "Sushi 81", AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.LegalIdentity, "90805211100014 FR03908052111 5610C", AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.Ticket, "20260913-001", "13/09/2026 12:30", AtomicGroup: "customer-ticket"),
            new(PrintReceiptBlockKind.Marker, "DUPLICATA"),
            new(PrintReceiptBlockKind.Item, "1 x P-001", "Produit avec une description très longue qui doit rester sur une seule ligne")
            {
                Item = new("1x", "P-001 Produit avec une description très longue qui doit rester sur une seule ligne", "10.00", string.Empty)
            },
            new(PrintReceiptBlockKind.Option, "Sauce", "1.50"),
            new(PrintReceiptBlockKind.Item, "2 x P-002", "Produit deux")
            {
                Item = new("2x", "P-002 Produit deux", "8.00", "16.00")
            },
            new(PrintReceiptBlockKind.Payment, "CB", "24.00", AtomicGroup: "customer-payment"),
            new(PrintReceiptBlockKind.PaymentConfirmation, "Payé en", "CB TVA incluse", AtomicGroup: "customer-payment"),
            new(PrintReceiptBlockKind.Total, "Total EUR", "24.00", AtomicGroup: "customer-total"),
            new(PrintReceiptBlockKind.Footer, "Merci de votre visite !", AtomicGroup: "customer-footer"),
            new(PrintReceiptBlockKind.Footer, "www.sushi81.fr", AtomicGroup: "customer-footer")
        ]);

        var result = await StaPrintThread.RunAsync(
            () =>
            {
                var rendered = ThermalPrintLayout.RenderPages(customerContent, new PrintImageableSurface(400, 600, 5, 5, 390, 580)).Single();
                var itemGrid = (Grid)ThermalPrintLayout.CreateVisual(rendered.First(block => block.Item is not null), ThermalPrintLayout.ThermalPrintableWidth72Mm);
                var totalGrid = ThermalPrintLayout.CreateVisual(rendered.Single(block => block.Total is not null), ThermalPrintLayout.ThermalPrintableWidth72Mm);
                var description = (TextBlock)itemGrid.Children[1];
                return (Rendered: rendered, DescriptionWrapping: description.TextWrapping, DescriptionTrimming: description.TextTrimming, TotalIsGrid: totalGrid is Grid);
            },
            CancellationToken.None);
        var rendered = result.Rendered;

        var ticket = rendered.Single(block => block.Text.Contains("20260913-001", StringComparison.Ordinal));
        var marker = rendered.Single(block => block.Text == "DUPLICATA");
        var itemRows = rendered.Where(block => block.Item is not null).ToArray();
        var total = rendered.Single(block => block.Total is not null);
        var payment = rendered.Single(block => block.Text.StartsWith("CB", StringComparison.Ordinal));
        var footerLines = rendered.Where(block => block.Text is "Merci de votre visite !" or "www.sushi81.fr").ToArray();

        Assert.AreEqual(ThermalPrintLayout.CustomerSectionGap, ticket.TopMargin);
        Assert.AreEqual(ThermalPrintLayout.CustomerSectionGap, marker.TopMargin);
        Assert.AreEqual(string.Empty, itemRows[0].Item!.LineTotalText);
        Assert.AreEqual("16.00", itemRows[1].Item!.LineTotalText);
        Assert.AreEqual(ThermalPrintLayout.CustomerTotalFontSize, total.FontSize);
        Assert.AreEqual(FontWeights.Bold, total.FontWeight);
        Assert.AreEqual("Total", total.Total!.Label);
        Assert.AreEqual("EUR 24.00", total.Total.Amount);
        Assert.AreEqual(TextAlignment.Left, payment.Alignment);
        Assert.HasCount(2, footerLines);
        Assert.AreEqual(ThermalPrintLayout.CustomerFooterGap, footerLines[0].TopMargin);
        Assert.AreEqual(0d, footerLines[1].TopMargin);

        Assert.AreEqual(TextWrapping.NoWrap, result.DescriptionWrapping);
        Assert.AreEqual(TextTrimming.CharacterEllipsis, result.DescriptionTrimming);
        Assert.IsTrue(result.TotalIsGrid);
    }

    [TestMethod]
    public async Task StructuredRendererExpressesR09CustomerOptionPriceColumnAndPreservesKitchenOptionText()
    {
        const string longOptionLabel = "Option avec un libelle tres long qui doit rester visible avant le montant";
        var customerContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Item, "1 x P-001", "Produit")
            {
                Item = new("1x", "P-001 Produit", "10.00", string.Empty)
            },
            new(PrintReceiptBlockKind.Option, longOptionLabel, "-1.00")
            {
                Option = new(longOptionLabel, "-1.00")
            },
            new(PrintReceiptBlockKind.Option, "Sauce premium", "1.50")
            {
                Option = new("Sauce premium", "1.50")
            }
        ]);
        var kitchenContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***"),
            new(PrintReceiptBlockKind.Option, "Option cuisine", "2.50 EUR")
        ]);

        var result = await StaPrintThread.RunAsync(
            () =>
            {
                var surface = new PrintImageableSurface(400, 600, 5, 5, 390, 580);
                var customer = ThermalPrintLayout.RenderPages(customerContent, surface).Single();
                var kitchen = ThermalPrintLayout.RenderPages(kitchenContent, surface).Single();
                var customerOption = customer.First(block => block.Option is not null);
                var optionGrid = (Grid)ThermalPrintLayout.CreateVisual(customerOption, ThermalPrintLayout.ThermalPrintableWidth72Mm);
                return (
                    Customer: customer,
                    Kitchen: kitchen,
                    Option: customerOption,
                    OptionColumnCount: optionGrid.ColumnDefinitions.Count,
                    DescriptionWrapping: ((TextBlock)optionGrid.Children[0]).TextWrapping,
                    DescriptionTrimming: ((TextBlock)optionGrid.Children[0]).TextTrimming,
                    AmountAlignment: ((TextBlock)optionGrid.Children[1]).TextAlignment,
                    AmountText: ((TextBlock)optionGrid.Children[1]).Text,
                    AmountColumn: Grid.GetColumn(optionGrid.Children[1]),
                    KitchenOption: kitchen.Single(block => block.Text.Contains("Option cuisine", StringComparison.Ordinal)),
                    KitchenVisualIsTextBlock: ThermalPrintLayout.CreateVisual(kitchen.Single(block => block.Text.Contains("Option cuisine", StringComparison.Ordinal)), ThermalPrintLayout.ThermalPrintableWidth72Mm) is TextBlock,
                    KitchenVisualText: ((TextBlock)ThermalPrintLayout.CreateVisual(kitchen.Single(block => block.Text.Contains("Option cuisine", StringComparison.Ordinal)), ThermalPrintLayout.ThermalPrintableWidth72Mm)).Text);
            },
            CancellationToken.None);

        Assert.IsTrue(result.Option.IsIndented);
        Assert.AreEqual(longOptionLabel, result.Option.Option!.Description);
        Assert.AreEqual("-1.00", result.Option.Option.AmountText);
        Assert.IsFalse(result.Option.Text.Contains("EUR", StringComparison.Ordinal));
        Assert.AreEqual(3, result.OptionColumnCount);
        Assert.AreEqual(TextWrapping.NoWrap, result.DescriptionWrapping);
        Assert.AreEqual(TextTrimming.CharacterEllipsis, result.DescriptionTrimming);
        Assert.AreEqual(TextAlignment.Right, result.AmountAlignment);
        Assert.AreEqual("-1.00", result.AmountText);
        Assert.AreEqual(2, result.AmountColumn);
        Assert.IsNull(result.KitchenOption.Option);
        Assert.IsTrue(result.KitchenOption.IsIndented);
        Assert.IsTrue(result.KitchenVisualIsTextBlock);
        StringAssert.Contains(result.KitchenVisualText, "Option cuisine 2.50 EUR");
    }

    [TestMethod]
    public async Task R10UsesArialThermalTiersAndSafe72MmContentGeometry()
    {
        var customerContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.BusinessName, "Sushi 81"),
            new(PrintReceiptBlockKind.LegalIdentity, "90805211100014 FR03908052111 5610C"),
            new(PrintReceiptBlockKind.Ticket, "20260913-001", "13/09/2026 12:30"),
            new(PrintReceiptBlockKind.Item, "1 x P-001", "Produit")
            {
                Item = new("1x", "P-001 Produit", "10.00", string.Empty)
            },
            new(PrintReceiptBlockKind.Option, "Sauce premium", "1.50")
            {
                Option = new("Sauce premium", "1.50")
            },
            new(PrintReceiptBlockKind.Total, "Total EUR", "11.50")
        ]);
        var kitchenContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***"),
            new(PrintReceiptBlockKind.LabelValue, "Cmd", "20260913-001"),
            new(PrintReceiptBlockKind.Item, "1x P-001", "Produit"),
            new(PrintReceiptBlockKind.Option, "Sauce premium", "1.50 EUR"),
            new(PrintReceiptBlockKind.Total, "TOTAL", "11.50 EUR")
        ]);

        var result = await StaPrintThread.RunAsync(
            () =>
            {
                var surface = new PrintImageableSurface(1000, 2400, 5, 5, 800, 2390);
                var contentWidth = ThermalPrintLayout.EffectiveContentWidth(surface);
                var contentOrigin = ThermalPrintLayout.EffectiveContentOriginWidth(surface);
                var customer = ThermalPrintLayout.RenderPages(customerContent, surface).Single();
                var kitchen = ThermalPrintLayout.RenderPages(kitchenContent, surface).Single();
                var customerItem = customer.Single(block => block.Item is not null);
                var customerOption = customer.Single(block => block.Option is not null);
                var customerTotal = customer.Single(block => block.Total is not null);
                var kitchenHeading = kitchen.Single(block => block.Text == "*** CUISINE ***");
                var kitchenBody = kitchen.Single(block => block.Text.Contains("Cmd", StringComparison.Ordinal));
                var kitchenTotal = kitchen.Single(block => block.Text.Contains("TOTAL", StringComparison.Ordinal));
                var itemGrid = (Grid)ThermalPrintLayout.CreateVisual(customerItem, contentWidth);
                var optionGrid = (Grid)ThermalPrintLayout.CreateVisual(customerOption, contentWidth);
                var totalGrid = (Grid)ThermalPrintLayout.CreateVisual(customerTotal, contentWidth);
                var ticketVisual = (TextBlock)ThermalPrintLayout.CreateVisual(customer.Single(block => block.Text.Contains("20260913-001", StringComparison.Ordinal)), contentWidth);
                var headingVisual = (TextBlock)ThermalPrintLayout.CreateVisual(kitchenHeading, contentWidth);
                var kitchenBodyVisual = (TextBlock)ThermalPrintLayout.CreateVisual(kitchenBody, contentWidth);
                var kitchenTotalVisual = (TextBlock)ThermalPrintLayout.CreateVisual(kitchenTotal, contentWidth);
                var itemPrice = itemGrid.Children.OfType<TextBlock>().Last();
                var optionAmount = optionGrid.Children.OfType<TextBlock>().Last();
                var totalAmount = totalGrid.Children.OfType<TextBlock>().Last();
                return (
                    ContentWidth: contentWidth,
                    ContentOrigin: contentOrigin,
                    RightSafeEdge: surface.OriginWidth
                        + (surface.ImageableWidth - ThermalPrintLayout.ThermalPrintableWidth72Mm) / 2
                        + ThermalPrintLayout.ThermalPrintableWidth72Mm
                        - ThermalPrintLayout.ThermalSafeInsetRight,
                    CustomerBusinessSize: customer.Single(block => block.Text == "Sushi 81").FontSize,
                    CustomerLegalSize: customer.Single(block => block.Text.StartsWith("90805211100014", StringComparison.Ordinal)).FontSize,
                    CustomerTicketSize: customer.Single(block => block.Text.Contains("20260913-001", StringComparison.Ordinal)).FontSize,
                    CustomerItemSize: customerItem.FontSize,
                    CustomerOptionSize: customerOption.FontSize,
                    CustomerTotalSize: customerTotal.FontSize,
                    KitchenHeadingSize: kitchenHeading.FontSize,
                    KitchenBodySize: kitchenBody.FontSize,
                    KitchenTotalSize: kitchenTotal.FontSize,
                    TicketFont: ticketVisual.FontFamily.Source,
                    HeadingFont: headingVisual.FontFamily.Source,
                    KitchenBodyFont: kitchenBodyVisual.FontFamily.Source,
                    ItemCellFont: itemPrice.FontFamily.Source,
                    OptionCellFont: optionAmount.FontFamily.Source,
                    TotalCellFont: totalAmount.FontFamily.Source,
                    TicketStretch: ticketVisual.FontStretch,
                    ItemStretch: itemPrice.FontStretch,
                    OptionStretch: optionAmount.FontStretch,
                    ItemGridWidth: itemGrid.Width,
                    OptionGridWidth: optionGrid.Width,
                    TotalGridWidth: totalGrid.Width,
                    ItemPriceColumn: Grid.GetColumn(itemPrice),
                    OptionPriceColumn: Grid.GetColumn(optionAmount),
                    TotalPriceColumn: Grid.GetColumn(totalAmount),
                    KitchenOptionPreserved: kitchen.Any(block => block.Text.Contains("Sauce premium 1.50 EUR", StringComparison.Ordinal)));
            },
            CancellationToken.None);

        Assert.AreEqual(
            ThermalPrintLayout.ThermalPrintableWidth72Mm - ThermalPrintLayout.ThermalSafeInsetLeft - ThermalPrintLayout.ThermalSafeInsetRight,
            result.ContentWidth,
            0.01);
        Assert.AreEqual(
            result.RightSafeEdge,
            result.ContentOrigin + result.ContentWidth,
            0.01);
        Assert.AreEqual(ThermalPrintLayout.CustomerBusinessNameFontSize, result.CustomerBusinessSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerLegalIdentityFontSize, result.CustomerLegalSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerBodyFontSize, result.CustomerTicketSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerBodyFontSize, result.CustomerItemSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerBodyFontSize, result.CustomerOptionSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerTotalFontSize, result.CustomerTotalSize);
        Assert.AreEqual(ThermalPrintLayout.KitchenHeadingFontSize, result.KitchenHeadingSize);
        Assert.AreEqual(ThermalPrintLayout.KitchenInfoFontSize, result.KitchenBodySize);
        Assert.AreEqual(ThermalPrintLayout.KitchenTotalFontSize, result.KitchenTotalSize);
        Assert.AreEqual(ThermalPrintLayout.ReceiptFontFamilyName, result.TicketFont);
        Assert.AreEqual(ThermalPrintLayout.ReceiptFontFamilyName, result.HeadingFont);
        Assert.AreEqual(ThermalPrintLayout.ReceiptFontFamilyName, result.KitchenBodyFont);
        Assert.AreEqual(ThermalPrintLayout.ReceiptFontFamilyName, result.ItemCellFont);
        Assert.AreEqual(ThermalPrintLayout.ReceiptFontFamilyName, result.OptionCellFont);
        Assert.AreEqual(ThermalPrintLayout.ReceiptFontFamilyName, result.TotalCellFont);
        Assert.AreEqual(FontStretches.Normal, result.TicketStretch);
        Assert.AreEqual(FontStretches.Normal, result.ItemStretch);
        Assert.AreEqual(FontStretches.Normal, result.OptionStretch);
        Assert.AreEqual(result.ContentWidth, result.ItemGridWidth);
        Assert.AreEqual(result.ContentWidth, result.OptionGridWidth);
        Assert.AreEqual(result.ContentWidth, result.TotalGridWidth);
        Assert.AreEqual(2, result.ItemPriceColumn);
        Assert.AreEqual(2, result.OptionPriceColumn);
        Assert.AreEqual(2, result.TotalPriceColumn);
        Assert.IsTrue(result.KitchenOptionPreserved);
    }

    [TestMethod]
    public async Task R11KeepsKitchenMetadataSmallAndSpacesLogicalRowsWithoutChangingCustomerTiers()
    {
        var kitchenContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***"),
            new(PrintReceiptBlockKind.Reference, "Cmd", "20260913-001"),
            new(PrintReceiptBlockKind.Timestamp, "Heure", "10:15"),
            new(PrintReceiptBlockKind.LabelValue, "Mode", "Livraison"),
            new(PrintReceiptBlockKind.Marker, "RÉIMPRESSION"),
            new(PrintReceiptBlockKind.Item, "1x P-001", "Produit avec un libellé suffisamment long pour forcer une continuation sur plusieurs lignes thermiques et conserver la preuve de l'espacement logique"),
            new(PrintReceiptBlockKind.Option, "Option cuisine avec un libellé suffisamment long pour forcer une continuation sur plusieurs lignes thermiques", "1.50 EUR"),
            new(PrintReceiptBlockKind.Total, "TOTAL", "11.50 EUR")
        ]);
        var customerContent = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Ticket, "20260913-001", "13/09/2026 12:30"),
            new(PrintReceiptBlockKind.Item, "1 x P-001", "Produit")
            {
                Item = new("1x", "P-001 Produit", "10.00", string.Empty)
            },
            new(PrintReceiptBlockKind.Total, "Total EUR", "10.00")
        ]);

        var result = await StaPrintThread.RunAsync(
            () =>
            {
                var surface = new PrintImageableSurface(400, 1200, 5, 5, 390, 1180);
                var kitchen = ThermalPrintLayout.RenderPages(kitchenContent, surface).Single();
                var customer = ThermalPrintLayout.RenderPages(customerContent, surface).Single();
                var customerItemVisual = ThermalPrintLayout.CreateVisual(customer.Single(block => block.Item is not null), ThermalPrintLayout.EffectiveContentWidth(surface));
                return (
                    Kitchen: kitchen,
                    Customer: customer,
                    CustomerItemFont: ((Grid)customerItemVisual).Children.OfType<TextBlock>().First().FontSize);
            },
            CancellationToken.None);

        var itemStart = Array.FindIndex(result.Kitchen.ToArray(), block => block.Text.StartsWith("1x P-001", StringComparison.Ordinal));
        var optionStart = Array.FindIndex(result.Kitchen.ToArray(), block => block.Text.StartsWith("- Option cuisine", StringComparison.Ordinal));
        var totalStart = Array.FindIndex(result.Kitchen.ToArray(), block => block.Text.Contains("TOTAL", StringComparison.Ordinal));
        var kitchenItemLines = result.Kitchen.Skip(itemStart).Take(optionStart - itemStart).ToArray();
        var kitchenOptionLines = result.Kitchen.Skip(optionStart).Take(totalStart - optionStart).ToArray();
        var kitchenHeading = result.Kitchen.Single(block => block.Text == "*** CUISINE ***");
        var kitchenReference = result.Kitchen.Single(block => block.Text.Contains("Cmd", StringComparison.Ordinal));
        var kitchenTimestamp = result.Kitchen.Single(block => block.Text.Contains("Heure", StringComparison.Ordinal));
        var kitchenInfo = result.Kitchen.Single(block => block.Text.Contains("Mode", StringComparison.Ordinal));
        var kitchenMarker = result.Kitchen.Single(block => block.Text == "RÉIMPRESSION");
        var kitchenTotal = result.Kitchen.Single(block => block.Text.Contains("TOTAL", StringComparison.Ordinal));

        Assert.AreEqual(ThermalPrintLayout.KitchenHeadingFontSize, kitchenHeading.FontSize);
        Assert.AreEqual(ThermalPrintLayout.KitchenInfoFontSize, kitchenReference.FontSize);
        Assert.AreEqual(ThermalPrintLayout.KitchenInfoFontSize, kitchenTimestamp.FontSize);
        Assert.AreEqual(ThermalPrintLayout.KitchenInfoFontSize, kitchenInfo.FontSize);
        Assert.AreEqual(ThermalPrintLayout.KitchenBodyFontSize, kitchenMarker.FontSize);
        Assert.AreEqual(FontWeights.Bold, kitchenMarker.FontWeight);
        Assert.IsGreaterThan(1, kitchenItemLines.Length);
        Assert.IsGreaterThan(1, kitchenOptionLines.Length);
        Assert.IsTrue(kitchenItemLines.All(block => block.FontSize == ThermalPrintLayout.KitchenBodyFontSize));
        Assert.IsTrue(kitchenOptionLines.All(block => block.FontSize == ThermalPrintLayout.KitchenBodyFontSize));
        Assert.AreEqual(ThermalPrintLayout.KitchenItemTopMargin, kitchenItemLines[0].TopMargin);
        Assert.IsTrue(kitchenItemLines.Skip(1).All(block => block.TopMargin == 0));
        Assert.AreEqual(ThermalPrintLayout.KitchenOptionTopMargin, kitchenOptionLines[0].TopMargin);
        Assert.IsTrue(kitchenOptionLines.Skip(1).All(block => block.TopMargin == 0));
        Assert.AreEqual(ThermalPrintLayout.KitchenTotalFontSize, kitchenTotal.FontSize);
        Assert.AreEqual(FontWeights.Bold, kitchenTotal.FontWeight);
        Assert.AreEqual(ThermalPrintLayout.CustomerBodyFontSize, result.Customer.Single(block => block.Text.Contains("20260913-001", StringComparison.Ordinal)).FontSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerBodyFontSize, result.Customer.Single(block => block.Item is not null).FontSize);
        Assert.AreEqual(ThermalPrintLayout.CustomerBodyFontSize, result.CustomerItemFont);
    }

    [TestMethod]
    public void ThermalLayoutUsesNormalAndNarrowDriverGeometryWithoutFallback()
    {
        var normal = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(5, 5, 210, 30));
        var narrow = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(1, 1, 50, 100));

        Assert.IsFalse(normal.UsedFallback);
        Assert.AreEqual(220d, normal.PageWidth);
        Assert.AreEqual(40d, normal.PageHeight);
        Assert.IsFalse(narrow.UsedFallback);
        Assert.AreEqual(52d, narrow.PageWidth);
        Assert.AreEqual(102d, narrow.PageHeight);
    }

    [TestMethod]
    public void ThermalLayoutUsesMediaOrBoundedFallbackForMissingAndInvalidImageableMetadata()
    {
        var mediaFallback = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(null, null, null, null, 320, 2400));
        var invalidFallback = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(-1, 0, double.NaN, double.PositiveInfinity, 9999, 99999));
        var defaultFallback = ThermalPrintLayout.FromGeometry(null);

        Assert.IsTrue(mediaFallback.UsedFallback);
        Assert.AreEqual(320d, mediaFallback.PageWidth);
        Assert.AreEqual(ThermalPrintLayout.MaximumFallbackPageHeight, mediaFallback.PageHeight);
        Assert.IsTrue(invalidFallback.UsedFallback);
        Assert.AreEqual(ThermalPrintLayout.MaximumFallbackPageWidth, invalidFallback.PageWidth);
        Assert.AreEqual(ThermalPrintLayout.MaximumFallbackPageHeight, invalidFallback.PageHeight);
        Assert.IsTrue(defaultFallback.UsedFallback);
        Assert.AreEqual(ThermalPrintLayout.FallbackPageWidth, defaultFallback.PageWidth);
        Assert.AreEqual(ThermalPrintLayout.FallbackPageHeight, defaultFallback.PageHeight);
        Assert.IsGreaterThan(0d, defaultFallback.ImageableWidth);
        Assert.IsGreaterThan(0d, defaultFallback.ImageableHeight);
    }

    [TestMethod]
    public async Task ProductionPrintThreadExecutesOnSta()
    {
        var apartmentState = await StaPrintThread.RunAsync(
            () => Thread.CurrentThread.GetApartmentState(),
            CancellationToken.None);

        Assert.AreEqual(ApartmentState.STA, apartmentState);
    }

    [TestMethod]
    public async Task KitchenHandwritingReserveOccupies25MmAfterFinalTotalOnLastPage()
    {
        var blocks = new List<PrintReceiptBlock>
        {
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***", AtomicGroup: "header")
        };
        for (var index = 0; index < 24; index++)
        {
            blocks.Add(new(PrintReceiptBlockKind.Item, $"1x P-{index:000}", "Plat suffisamment long pour enrouler le texte"));
            blocks.Add(new(PrintReceiptBlockKind.Option, $"Option-{index:000}", "0.50"));
        }
        blocks.Add(new(PrintReceiptBlockKind.Total, "TOTAL", "23.67 EUR", AtomicGroup: "kitchen-total"));
        blocks.Add(new(PrintReceiptBlockKind.HandwritingSpace, string.Empty, AtomicGroup: "kitchen-total"));
        var content = new PrintReceiptContent(blocks);

        var result = await StaPrintThread.RunAsync(() =>
        {
            var surface = new PrintImageableSurface(280, 280, 5, 5, 270, 230);
            var width = ThermalPrintLayout.EffectiveContentWidth(surface);
            var pages = ThermalPrintLayout.RenderPages(content, surface);
            var last = pages[^1];
            var spacer = last[^1];
            var withoutSpacer = ThermalPrintLayout.MeasureRenderedHeight(last.Take(last.Count - 1), width);
            var withSpacer = ThermalPrintLayout.MeasureRenderedHeight(last, width);
            return (PageCount: pages.Count,
                ReserveCount: pages.Sum(page => page.Count(block => block.SpacerHeight > 0)),
                EarlierReserveCount: pages.Take(pages.Count - 1).Sum(page => page.Count(block => block.SpacerHeight > 0)),
                LastReserveCount: last.Count(block => block.SpacerHeight > 0),
                LastTotalText: last[^2].Text, LastTotalGroup: last[^2].AtomicGroup,
                SpacerText: spacer.Text, SpacerGroup: spacer.AtomicGroup, SpacerHeight: spacer.SpacerHeight,
                ExtraHeight: withSpacer - withoutSpacer,
                PagesWithinBounds: pages.All(page => ThermalPrintLayout.MeasureRenderedHeight(page, width) <= surface.ImageableHeight + 0.01),
                Diagnostics: ThermalPrintLayout.Paginate(content, surface),
                RenderedText: string.Join(" ", pages.SelectMany(page => page).Select(block => block.Text)));
        }, CancellationToken.None);

        Assert.IsGreaterThan(1, result.PageCount);
        Assert.AreEqual(1, result.ReserveCount);
        Assert.AreEqual(0, result.EarlierReserveCount);
        Assert.AreEqual(1, result.LastReserveCount);
        StringAssert.Contains(result.LastTotalText, "TOTAL : 23.67 EUR");
        Assert.AreEqual("kitchen-total", result.LastTotalGroup);
        Assert.AreEqual(result.LastTotalGroup, result.SpacerGroup);
        Assert.AreEqual(string.Empty, result.SpacerText);
        Assert.AreEqual(25d / 25.4d * 96d, result.SpacerHeight, 0.01);
        Assert.AreEqual(result.SpacerHeight, result.ExtraHeight, 0.01);
        Assert.IsTrue(result.PagesWithinBounds, "The total and physical reserve must not exceed any page's printable height.");
        Assert.IsTrue(result.Diagnostics[^1].EndsWith("TOTAL : 23.67 EUR", StringComparison.Ordinal));
        for (var index = 0; index < 24; index++)
        {
            StringAssert.Contains(result.RenderedText, $"P-{index:000}");
            StringAssert.Contains(result.RenderedText, $"Option-{index:000}");
        }
    }

    [TestMethod]
    public async Task KitchenHandwritingReservePaintsOnlyTheBottomRightFeedAnchorAtNormalAndNarrowWidths()
    {
        var evidence = await StaPrintThread.RunAsync(() =>
        {
            var content = KitchenBoundaryContent();
            var surface = new PrintImageableSurface(280, 500, 5, 5, 270, 480);
            var spacer = ThermalPrintLayout.RenderPages(content, surface).Single()[^1];
            return new[] { ThermalPrintLayout.EffectiveContentWidth(surface), 40d, 1d }
                .Select(width => RasterizeReserve(spacer, width)).ToArray();
        }, CancellationToken.None);

        foreach (var raster in evidence)
            AssertReserveRaster(raster);
    }

    [TestMethod]
    public async Task KitchenTotalAndReserveStayTogetherAtTheExactPaginationBoundary()
    {
        var result = await StaPrintThread.RunAsync(() =>
        {
            var content = KitchenBoundaryContent();
            var large = new PrintImageableSurface(280, 500, 5, 5, 270, 480);
            var width = ThermalPrintLayout.EffectiveContentWidth(large);
            var all = ThermalPrintLayout.RenderPages(content, large).Single();
            var exactHeight = ThermalPrintLayout.MeasureRenderedHeight(all, width);
            var exact = ThermalPrintLayout.RenderPages(content, large with { ImageableHeight = exactHeight });
            var moved = ThermalPrintLayout.RenderPages(content, large with { ImageableHeight = exactHeight - 0.5 });
            var final = moved[^1];
            return (ExactPageCount: exact.Count, MovedPageCount: moved.Count,
                ExactReserveCount: exact.SelectMany(page => page).Count(block => block.SpacerHeight > 0),
                EarlierReserveCount: moved.Take(moved.Count - 1).SelectMany(page => page).Count(block => block.SpacerHeight > 0),
                FinalBlockCount: final.Count, FinalTotalText: final[^2].Text,
                FinalReserveHeight: final[^1].SpacerHeight,
                SameGroup: final[^2].AtomicGroup == final[^1].AtomicGroup,
                PagesWithinBounds: moved.All(page => ThermalPrintLayout.MeasureRenderedHeight(page, width) <= exactHeight - 0.5 + 0.01));
        }, CancellationToken.None);

        Assert.AreEqual(1, result.ExactPageCount, "An exactly fitting total and reserve must remain on the current page.");
        Assert.AreEqual(2, result.MovedPageCount);
        Assert.AreEqual(1, result.ExactReserveCount);
        Assert.AreEqual(0, result.EarlierReserveCount);
        Assert.AreEqual(2, result.FinalBlockCount, "The final page must contain both TOTAL and its reserve, never a reserve-only page.");
        Assert.AreEqual("TOTAL : 12.50 EUR", result.FinalTotalText);
        Assert.AreEqual(25d / 25.4d * 96d, result.FinalReserveHeight, 0.01);
        Assert.IsTrue(result.SameGroup);
        Assert.IsTrue(result.PagesWithinBounds);
    }

    [TestMethod]
    public async Task KitchenTotalAndReserveRejectAPrintablePageTooShortForTheirAtomicGroup()
    {
        var message = await StaPrintThread.RunAsync(() =>
        {
            var content = KitchenBoundaryContent();
            var large = new PrintImageableSurface(280, 500, 5, 5, 270, 480);
            var width = ThermalPrintLayout.EffectiveContentWidth(large);
            var rendered = ThermalPrintLayout.RenderPages(content, large).Single();
            var footerHeight = ThermalPrintLayout.MeasureRenderedHeight(rendered.TakeLast(2), width);
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
                ThermalPrintLayout.RenderPages(content, large with { ImageableHeight = footerHeight - 0.5 }));
            return exception.Message;
        }, CancellationToken.None);

        StringAssert.Contains(message, "kitchen total and physical handwriting reserve");
    }

    [TestMethod]
    public async Task EveryKitchenFactoryVariantKeepsThePhysicalReserveWithoutChangingCustomerOrDiagnosticText()
    {
        var variantCount = await StaPrintThread.RunAsync(() =>
        {
            var count = 0;
            var surface = new PrintImageableSurface(280, 1200, 5, 5, 270, 1180);
            var width = ThermalPrintLayout.EffectiveContentWidth(surface);
            foreach (var status in new[] { OrderStatus.Open, OrderStatus.Cancelled })
            foreach (var intent in new[] { PrintIntent.InitialAutomatic, PrintIntent.InitialRetry, PrintIntent.ExplicitReprint })
            foreach (var profile in new[] { DeploymentProfile.Production, DeploymentProfile.PreProduction })
            {
                var factory = new OrderPrintDocumentFactory(new FixedClock(), profile);
                var order = SyntheticKitchenOrder(status);
                var kitchen = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Kitchen, intent);
                var kitchenDiagnostic = kitchen.Text;
                var kitchenBusinessContent = new PrintReceiptContent(kitchen.Content.Blocks
                    .Where(block => block.Kind != PrintReceiptBlockKind.HandwritingSpace).ToArray());
                Assert.AreEqual(kitchenBusinessContent.ToDiagnosticText(), kitchenDiagnostic,
                    "The physical reserve must not add business text or a synthetic blank line.");

                var pages = ThermalPrintLayout.RenderPages(kitchen.Content, surface);
                var final = pages[^1];
                Assert.AreEqual(1, pages.SelectMany(page => page).Count(block => block.SpacerHeight > 0));
                Assert.AreEqual(0, pages.Take(pages.Count - 1).SelectMany(page => page).Count(block => block.SpacerHeight > 0));
                Assert.AreEqual("TOTAL : 12.50 EUR", final[^2].Text);
                Assert.AreEqual(final[^2].AtomicGroup, final[^1].AtomicGroup);
                Assert.AreEqual(25d / 25.4d * 96d, final[^1].SpacerHeight, 0.01);
                Assert.IsTrue(pages.All(page => ThermalPrintLayout.MeasureRenderedHeight(page, width) <= surface.ImageableHeight + 0.01));
                AssertReserveRaster(RasterizeReserve(final[^1], width));
                Assert.AreEqual(kitchenDiagnostic, kitchen.Content.ToDiagnosticText());
                Assert.IsTrue(ThermalPrintLayout.Paginate(kitchen.Content, surface)[^1].EndsWith("TOTAL : 12.50 EUR", StringComparison.Ordinal));

                var customer = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, intent);
                var customerDiagnostic = customer.Text;
                var customerRendered = ThermalPrintLayout.RenderPages(customer.Content, surface).SelectMany(page => page).ToArray();
                Assert.AreEqual(0, customer.Content.Blocks.Count(block => block.Kind == PrintReceiptBlockKind.HandwritingSpace));
                Assert.AreEqual(0, customerRendered.Count(block => block.SpacerHeight > 0));
                Assert.AreEqual("Total EUR 12.50", customerRendered.Single(block => block.Total is not null).Text);
                Assert.AreEqual(customerDiagnostic, customer.Content.ToDiagnosticText());
                Assert.AreEqual(profile.IsPreProduction, customerDiagnostic.Contains("*** PREPROD ***", StringComparison.Ordinal));
                Assert.AreEqual(status == OrderStatus.Cancelled, customerDiagnostic.Contains("ANNULÉ", StringComparison.Ordinal));
                Assert.AreEqual(intent == PrintIntent.ExplicitReprint, customerDiagnostic.Contains("DUPLICATA", StringComparison.Ordinal));
                StringAssert.Contains(kitchenDiagnostic, "H17-001");
                StringAssert.Contains(kitchenDiagnostic, "Option synthétique");
                StringAssert.Contains(customerDiagnostic, "H17-001");
                StringAssert.Contains(customerDiagnostic, "Option synthétique");
                count++;
            }
            return count;
        }, CancellationToken.None);

        Assert.AreEqual(12, variantCount);
    }

    [TestMethod]
    public async Task CustomerDiscountRendersOneNegativeRemiseLineWithoutKitchenSpacer()
    {
        var content = new PrintReceiptContent([
            new(PrintReceiptBlockKind.BusinessName, "Sushi 81"),
            new(PrintReceiptBlockKind.Discount, "Remise", "-2.63 EUR", AtomicGroup: "customer-total"),
            new(PrintReceiptBlockKind.Total, "Total EUR", "23.67", AtomicGroup: "customer-total")
        ]);

        var rendered = await StaPrintThread.RunAsync(() =>
            ThermalPrintLayout.RenderPages(content, new PrintImageableSurface(300, 300, 5, 5, 290, 280))
                .SelectMany(page => page).ToArray(), CancellationToken.None);

        Assert.AreEqual(1, rendered.Count(block => block.Text == "Remise : -2.63 EUR"));
        Assert.AreEqual(0, rendered.Count(block => block.SpacerHeight > 0));
        Assert.AreEqual("Total EUR 23.67", rendered.Single(block => block.Total is not null).Text);
    }

    [TestMethod]
    public void PrintQueueSelectionNormalizesSortsAndResolvesStableIdentifiers()
    {
        var queues = PrintQueueSelection.Normalize([
            new PrintQueueInfo("z-id", "Zeta"),
            new PrintQueueInfo("a-id", "Alpha"),
            new PrintQueueInfo("A-ID", "Alpha alias"),
            new PrintQueueInfo("", "NameOnly"),
            new PrintQueueInfo("", "nameonly"),
            new PrintQueueInfo("", "")
        ]);

        Assert.HasCount(3, queues);
        Assert.AreEqual("Alpha", queues[0].Name);
        Assert.AreEqual("NameOnly", queues[1].Name);
        Assert.AreEqual("Zeta", queues[2].Name);
        Assert.IsTrue(PrintQueueSelection.Matches(queues[0], "a-id", null));
        Assert.IsTrue(PrintQueueSelection.Matches(queues[0], null, "Alpha"));
        Assert.IsTrue(PrintQueueSelection.Matches(queues[0], null, "a-id"));
        Assert.IsFalse(PrintQueueSelection.Matches(queues[0], "missing", "missing"));
    }

    [TestMethod]
    public void PrintQueueSelectionLeavesAnUnmatchedConfiguredQueueUnavailable()
    {
        var queues = PrintQueueSelection.Normalize([
            new PrintQueueInfo("pdf-id", "Microsoft Print to PDF"),
            new PrintQueueInfo("brother-id", "Brother HL-L2400DWE Printer")
        ]);

        Assert.IsFalse(queues.Any(queue => PrintQueueSelection.Matches(queue, "missing-id", "Missing queue")));
    }

    private static PrintReceiptContent KitchenBoundaryContent() => new([
        new(PrintReceiptBlockKind.Heading, "*** CUISINE ***", AtomicGroup: "kitchen-header"),
        new(PrintReceiptBlockKind.Reference, "Cmd", "H17-001", AtomicGroup: "kitchen-header"),
        new(PrintReceiptBlockKind.Total, "TOTAL", "12.50 EUR", AtomicGroup: "kitchen-total"),
        new(PrintReceiptBlockKind.HandwritingSpace, string.Empty, AtomicGroup: "kitchen-total")
    ]);

    private static ReserveRaster RasterizeReserve(RenderedThermalReceiptBlock block, double width)
    {
        var visual = ThermalPrintLayout.CreateVisual(block, width);
        Assert.IsInstanceOfType<Border>(visual);
        var reserve = (Border)visual;
        Assert.IsInstanceOfType<Border>(reserve.Child);
        var anchor = (Border)reserve.Child;
        Assert.IsNull(anchor.Child, "The feed anchor is a painted shape, not another business-text row.");
        visual.Measure(new Size(width, double.PositiveInfinity));
        visual.Arrange(new Rect(0, 0, width, visual.DesiredSize.Height));
        visual.UpdateLayout();
        var anchorBounds = anchor.TransformToAncestor(visual).TransformBounds(new Rect(anchor.RenderSize));
        var pixelWidth = (int)Math.Ceiling(width);
        var pixelHeight = (int)Math.Ceiling(visual.RenderSize.Height);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[pixelWidth * pixelHeight * 4];
        bitmap.CopyPixels(pixels, pixelWidth * 4, 0);
        return new(width, visual.DesiredSize.Height, visual.RenderSize.Height, anchorBounds,
            reserve.ClipToBounds, anchor.Background is SolidColorBrush brush && brush.Color == Colors.Black,
            pixelWidth, pixelHeight, pixels);
    }

    private static void AssertReserveRaster(ReserveRaster raster)
    {
        const double height = 25d / 25.4d * 96d;
        var anchorWidth = Math.Min(2d, raster.Width);
        Assert.AreEqual(height, raster.DesiredHeight, 0.01);
        Assert.AreEqual(height, raster.ActualHeight, 0.01);
        Assert.IsTrue(raster.ReserveClipsToBounds);
        Assert.IsTrue(raster.AnchorIsBlack);
        Assert.AreEqual(anchorWidth, raster.AnchorBounds.Width, 0.01);
        Assert.AreEqual(2d, raster.AnchorBounds.Height, 0.01);
        Assert.AreEqual(raster.Width - anchorWidth, raster.AnchorBounds.Left, 0.01);
        Assert.AreEqual(height - 2d, raster.AnchorBounds.Top, 0.01);
        Assert.AreEqual(raster.Width, raster.AnchorBounds.Right, 0.01);
        Assert.AreEqual(height, raster.AnchorBounds.Bottom, 0.01);

        var inkCount = 0;
        var opaqueInkCount = 0;
        var lastInkX = -1;
        var lastInkY = -1;
        for (var y = 0; y < raster.PixelHeight; y++)
        for (var x = 0; x < raster.PixelWidth; x++)
        {
            var offset = (y * raster.PixelWidth + x) * 4;
            var alpha = raster.Pixels[offset + 3];
            // RenderTargetBitmap begins transparent; RGB zero without alpha is not ink.
            if (alpha == 0)
                continue;
            Assert.IsTrue(raster.Pixels[offset] <= 16 && raster.Pixels[offset + 1] <= 16 && raster.Pixels[offset + 2] <= 16,
                "The reserve may paint only its black feed anchor, with no gray or colored handwriting-area background.");
            inkCount++;
            if (alpha == 255) opaqueInkCount++;
            lastInkX = Math.Max(lastInkX, x);
            lastInkY = Math.Max(lastInkY, y);
            Assert.IsTrue(x >= Math.Floor(raster.AnchorBounds.Left) - 1 &&
                y >= Math.Floor(raster.AnchorBounds.Top) - 1,
                "The handwriting reserve interior must remain ink-free; only the bottom-right anchor may paint.");
        }
        Assert.IsGreaterThan(0, inkCount, "A measured but unpainted reserve can be trimmed by a thermal driver.");
        Assert.IsGreaterThan(0, opaqueInkCount);
        Assert.IsLessThanOrEqualTo(16, inkCount, "The anchor must remain a small painted shape rather than a line or label.");
        Assert.IsGreaterThanOrEqualTo((int)Math.Floor(raster.Width) - 1, lastInkX);
        Assert.IsGreaterThanOrEqualTo((int)Math.Floor(height) - 1, lastInkY);
    }

    private sealed record ReserveRaster(double Width, double DesiredHeight, double ActualHeight,
        Rect AnchorBounds, bool ReserveClipsToBounds, bool AnchorIsBlack,
        int PixelWidth, int PixelHeight, byte[] Pixels);

    private static OrderSnapshot SyntheticKitchenOrder(OrderStatus status)
    {
        var now = new DateTimeOffset(2026, 9, 12, 10, 15, 0, TimeSpan.Zero);
        return new(
            Guid.Parse("11111111-1111-1111-1111-111111111117"), OrderSourceType.Pos, status,
            now, now, null, status == OrderStatus.Cancelled ? now : null,
            FulfilmentMode.Livraison, new DateOnly(2026, 9, 12), new TimeOnly(18, 30), false,
            "00 00 00 00 00", "1 rue Synthétique", "Note synthétique", Money.FromCents(1250),
            false, false, null, Money.Zero,
            [new(Guid.Parse("22222222-2222-2222-2222-222222222217"), 0,
                Guid.Parse("33333333-3333-3333-3333-333333333317"), "H17-001", "Plat synthétique", "Plats",
                Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1250),
                [new(Guid.Parse("44444444-4444-4444-4444-444444444417"), 0, OrderAdjustmentKind.PredefinedOption,
                    Guid.Parse("55555555-5555-5555-5555-555555555517"), "Options", "Option synthétique",
                    Money.FromCents(250), 10m)])],
            [new(10m, Money.FromCents(1250), Money.FromCents(113))])
        { Reference = "20260912-017" };
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 12, 10, 15, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 12);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TempPaths : IAppPaths, IDisposable
    {
        public TempPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M08.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            EnsureInitialized();
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
