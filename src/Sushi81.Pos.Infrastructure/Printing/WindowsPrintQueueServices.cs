using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Infrastructure.Printing;

public sealed class WindowsPrintQueueCatalog : IPrintQueueCatalog
{
    public Task<IReadOnlyList<PrintQueueInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        StaPrintThread.RunAsync<IReadOnlyList<PrintQueueInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = new LocalPrintServer();
            var result = new List<PrintQueueInfo>();
            foreach (var queue in server.GetPrintQueues())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Add(new(queue.FullName, queue.Name));
                }
                finally
                {
                    queue.Dispose();
                }
            }
            return PrintQueueSelection.Normalize(result);
        }, cancellationToken);
}

/// <summary>Windows/WPF print boundary. It does not expose driver diagnostics to operators.</summary>
public sealed class WindowsPrintDocumentSubmitter : IPrintDocumentSubmitter
{
    public async Task<PrintOutcomeStatus> SubmitAsync(
        OrderPrintDocument document,
        string? configuredQueueId,
        string? configuredQueueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return await StaPrintThread.RunAsync(
            () => SubmitCore(document, configuredQueueId, configuredQueueName, cancellationToken),
            cancellationToken);
    }

    private static PrintOutcomeStatus SubmitCore(OrderPrintDocument document, string? configuredQueueId, string? configuredQueueName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredQueueId) && string.IsNullOrWhiteSpace(configuredQueueName))
            return PrintOutcomeStatus.QueueUnavailable;

        PrintQueue? queue = null;
        var writeStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = new LocalPrintServer();
            foreach (var candidate in server.GetPrintQueues())
            {
                if (PrintQueueSelection.Matches(new(candidate.FullName, candidate.Name), configuredQueueId, configuredQueueName))
                {
                    queue = candidate;
                    break;
                }

                candidate.Dispose();
            }

            if (queue is null) return PrintOutcomeStatus.QueueUnavailable;

            var capabilities = queue.GetPrintCapabilities();
            var documentPaginator = CreateDocumentPaginator(document, capabilities);
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writeStarted = true;
            writer.Write(documentPaginator);
            return PrintOutcomeStatus.Succeeded;
        }
        catch (OperationCanceledException)
        {
            return writeStarted ? PrintOutcomeStatus.AmbiguousSubmission : PrintOutcomeStatus.SubmissionFailed;
        }
        catch (PrintQueueException)
        {
            return writeStarted ? PrintOutcomeStatus.AmbiguousSubmission : PrintOutcomeStatus.SubmissionFailed;
        }
        catch (Exception) when (writeStarted)
        {
            return PrintOutcomeStatus.AmbiguousSubmission;
        }
        catch (Exception)
        {
            return PrintOutcomeStatus.SubmissionFailed;
        }
        finally
        {
            queue?.Dispose();
        }
    }

    private static FixedDocument CreateDocumentPaginator(OrderPrintDocument document, PrintCapabilities capabilities)
    {
        var surface = ThermalPrintLayout.From(capabilities);
        var pages = ThermalPrintLayout.RenderPages(document.Content, surface);
        var contentWidth = ThermalPrintLayout.EffectiveContentWidth(surface);
        var contentOrigin = ThermalPrintLayout.EffectiveContentOriginWidth(surface);
        var fixedDocument = new FixedDocument();
        foreach (var pageBlocks in pages)
        {
            var contentHeight = ThermalPrintLayout.MeasureRenderedHeight(pageBlocks, contentWidth);
            var bottomMargin = Math.Max(0, surface.PageHeight - surface.OriginHeight - surface.ImageableHeight);
            var pageHeight = Math.Min(surface.PageHeight, surface.OriginHeight + contentHeight + bottomMargin);
            var page = new FixedPage { Width = surface.PageWidth, Height = pageHeight };
            var panel = new StackPanel
            {
                Width = contentWidth,
                Margin = new Thickness(contentOrigin, surface.OriginHeight, 0, 0)
            };
            foreach (var block in pageBlocks)
                panel.Children.Add(ThermalPrintLayout.CreateVisual(block, contentWidth));
            page.Children.Add(panel);
            var pageContent = new PageContent();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
            fixedDocument.Pages.Add(pageContent);
        }
        return fixedDocument;
    }
}

internal static class PrintQueueSelection
{
    public static IReadOnlyList<PrintQueueInfo> Normalize(IEnumerable<PrintQueueInfo> queues)
    {
        ArgumentNullException.ThrowIfNull(queues);

        return queues
            .Where(queue => !string.IsNullOrWhiteSpace(queue.Id) || !string.IsNullOrWhiteSpace(queue.Name))
            .GroupBy(StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(queue => queue.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(queue => queue.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(queue => queue.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(queue => queue.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool Matches(PrintQueueInfo queue, string? configuredQueueId, string? configuredQueueName)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return string.Equals(queue.Id, configuredQueueId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(queue.Name, configuredQueueName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(queue.Id, configuredQueueName, StringComparison.OrdinalIgnoreCase);
    }

    private static string StableKey(PrintQueueInfo queue) =>
        !string.IsNullOrWhiteSpace(queue.Id)
            ? $"id:{queue.Id}"
            : $"name:{queue.Name}";
}

public sealed record PrintImageableSurface(
    double PageWidth,
    double PageHeight,
    double OriginWidth,
    double OriginHeight,
    double ImageableWidth,
    double ImageableHeight)
{
    public bool UsedFallback { get; init; }
}

/// <summary>Testable snapshot of the geometry exposed by a Windows print driver.</summary>
public sealed record PrintImageableGeometry(
    double? OriginWidth,
    double? OriginHeight,
    double? ImageableWidth,
    double? ImageableHeight,
    double? MediaWidth = null,
    double? MediaHeight = null);

/// <summary>
/// Converts the selected queue's actual imageable area into bounded receipt pages. Queue
/// capabilities own normal geometry; a bounded thermal-safe fallback is used only when the
/// driver omits or corrupts imageable metadata.
/// </summary>
public static class ThermalPrintLayout
{
    public const string ReceiptFontFamilyName = "Arial";
    public const double CustomerBodyFontSize = 12;
    public const double FontSize = CustomerBodyFontSize;
    public const double KitchenBodyFontSize = 18;
    public const double KitchenHeadingFontSize = 22;
    public const double KitchenTotalFontSize = 20;
    public const double CustomerBusinessNameFontSize = 17;
    public const double CustomerLegalIdentityFontSize = 10;
    public const double CustomerTotalFontSize = 17;
    public const double CustomerSectionGap = CustomerBodyFontSize;
    public const double CustomerFooterGap = CustomerBodyFontSize * 2;
    public const double ThermalRollWidth80Mm = 80d / 25.4d * 96d;
    public const double ThermalPrintableWidth72Mm = 72d / 25.4d * 96d;
    public const double ThermalSafeInsetLeftMm = 2;
    public const double ThermalSafeInsetRightMm = 3;
    public const double ThermalSafeInsetLeft = ThermalSafeInsetLeftMm / 25.4d * 96d;
    public const double ThermalSafeInsetRight = ThermalSafeInsetRightMm / 25.4d * 96d;
    public const double FallbackPageWidth = 288;
    public const double FallbackPageHeight = 1440;
    public const double MinimumFallbackPageWidth = 72;
    public const double MaximumFallbackPageWidth = 576;
    public const double MinimumFallbackPageHeight = 144;
    public const double MaximumFallbackPageHeight = 1440;
    private const double FallbackMargin = 9;

    public static double EffectiveContentWidth(PrintImageableSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var cappedWidth = Math.Min(surface.ImageableWidth, ThermalPrintableWidth72Mm);
        return Math.Max(1, cappedWidth - EffectiveLeftInset(cappedWidth) - EffectiveRightInset(cappedWidth));
    }

    public static double EffectiveContentOriginWidth(PrintImageableSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var cappedWidth = Math.Min(surface.ImageableWidth, ThermalPrintableWidth72Mm);
        var centeredOrigin = surface.OriginWidth + Math.Max(0, (surface.ImageableWidth - cappedWidth) / 2);
        return centeredOrigin + EffectiveLeftInset(cappedWidth);
    }

    private static double EffectiveLeftInset(double cappedWidth) =>
        Math.Min(ThermalSafeInsetLeft, Math.Max(0, cappedWidth - 1));

    private static double EffectiveRightInset(double cappedWidth)
    {
        var leftInset = EffectiveLeftInset(cappedWidth);
        return Math.Min(ThermalSafeInsetRight, Math.Max(0, cappedWidth - leftInset - 1));
    }

    public static PrintImageableSurface From(PrintCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var media = capabilities.PageMediaSizeCapability
            .Where(candidate => candidate.Width is > 0 && candidate.Height is > 0)
            .OrderByDescending(candidate => candidate.Width!.Value * candidate.Height!.Value)
            .FirstOrDefault();
        var imageable = capabilities.PageImageableArea;
        var geometry = imageable is null
            ? new PrintImageableGeometry(null, null, null, null, media?.Width, media?.Height)
            : new PrintImageableGeometry(
                imageable.OriginWidth,
                imageable.OriginHeight,
                imageable.ExtentWidth,
                imageable.ExtentHeight,
                media?.Width,
                media?.Height);
        return FromGeometry(geometry);
    }

    public static PrintImageableSurface FromGeometry(PrintImageableGeometry? geometry)
    {
        if (geometry is not null && TryCreateSurface(geometry, out var surface)) return surface;

        var mediaWidth = geometry?.MediaWidth;
        var mediaHeight = geometry?.MediaHeight;
        return CreateFallbackSurface(mediaWidth, mediaHeight);
    }

    private static bool TryCreateSurface(PrintImageableGeometry geometry, out PrintImageableSurface surface)
    {
        surface = null!;
        if (!IsFiniteNonNegative(geometry.OriginWidth) || !IsFiniteNonNegative(geometry.OriginHeight)
            || !IsFinitePositive(geometry.ImageableWidth) || !IsFinitePositive(geometry.ImageableHeight)) return false;

        var pageWidth = geometry.ImageableWidth!.Value + geometry.OriginWidth!.Value * 2;
        var pageHeight = geometry.ImageableHeight!.Value + geometry.OriginHeight!.Value * 2;
        if (!IsFinitePositive(pageWidth) || !IsFinitePositive(pageHeight)
            || pageWidth > MaximumFallbackPageWidth || pageHeight > MaximumFallbackPageHeight) return false;

        surface = new(
            pageWidth,
            pageHeight,
            geometry.OriginWidth.Value,
            geometry.OriginHeight.Value,
            geometry.ImageableWidth.Value,
            geometry.ImageableHeight.Value);
        return true;
    }

    private static PrintImageableSurface CreateFallbackSurface(double? mediaWidth, double? mediaHeight)
    {
        var pageWidth = Clamp(mediaWidth ?? FallbackPageWidth, MinimumFallbackPageWidth, MaximumFallbackPageWidth);
        var pageHeight = Clamp(mediaHeight ?? FallbackPageHeight, MinimumFallbackPageHeight, MaximumFallbackPageHeight);
        var horizontalMargin = Math.Min(FallbackMargin, pageWidth / 4);
        var verticalMargin = Math.Min(FallbackMargin, pageHeight / 4);
        return new(
            pageWidth,
            pageHeight,
            horizontalMargin,
            verticalMargin,
            pageWidth - horizontalMargin * 2,
            pageHeight - verticalMargin * 2)
        {
            UsedFallback = true
        };
    }

    private static bool IsFinitePositive(double? value) => value is { } number && double.IsFinite(number) && number > 0;
    private static bool IsFiniteNonNegative(double? value) => value is { } number && double.IsFinite(number) && number >= 0;
    private static double Clamp(double value, double minimum, double maximum) => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;

    public static IReadOnlyList<string> Paginate(string text, PrintImageableSurface surface)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(surface);
        var contentWidth = EffectiveContentWidth(surface);
        var lineHeight = MeasureHeight("M", contentWidth);
        var wrappedLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .SelectMany(line => WrapLine(line, contentWidth, lineHeight))
            .ToArray();
        var pages = new List<string>();
        var current = new List<string>();
        foreach (var line in wrappedLines)
        {
            var candidate = string.Join(Environment.NewLine, current.Append(line));
            if (current.Count > 0 && MeasureHeight(candidate, contentWidth) > surface.ImageableHeight)
            {
                pages.Add(string.Join(Environment.NewLine, current));
                current.Clear();
            }
            current.Add(line);
        }
        if (current.Count > 0) pages.Add(string.Join(Environment.NewLine, current));
        return pages.Count == 0 ? [string.Empty] : pages;
    }

    public static IReadOnlyList<string> Paginate(PrintReceiptContent content, PrintImageableSurface surface)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(surface);
        return RenderPages(content, surface)
            .Select(page => string.Join(Environment.NewLine, page.Select(block => block.Text)))
            .DefaultIfEmpty(string.Empty)
            .ToArray();
    }

    internal static IReadOnlyList<IReadOnlyList<RenderedThermalReceiptBlock>> RenderPages(PrintReceiptContent content, PrintImageableSurface surface)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(surface);

        var width = EffectiveContentWidth(surface);
        var renderedBlocks = ThermalReceiptRenderer.Render(content, width);
        var units = new List<IReadOnlyList<RenderedThermalReceiptBlock>>();
        foreach (var block in renderedBlocks)
        {
            if (block.AtomicGroup is not null
                && units.Count > 0
                && units[^1].Count > 0
                && units[^1][0].AtomicGroup == block.AtomicGroup)
            {
                units[^1] = units[^1].Append(block).ToArray();
            }
            else
            {
                units.Add([block]);
            }
        }

        var pages = new List<IReadOnlyList<RenderedThermalReceiptBlock>>();
        var current = new List<RenderedThermalReceiptBlock>();
        foreach (var unit in units)
        {
            var candidate = current.Concat(unit).ToArray();
            if (current.Count > 0 && MeasureRenderedHeight(candidate, width) > surface.ImageableHeight)
            {
                pages.Add(current.ToArray());
                current.Clear();
            }

            if (MeasureRenderedHeight(unit, width) <= surface.ImageableHeight)
            {
                current.AddRange(unit);
                continue;
            }

            foreach (var block in unit)
            {
                if (current.Count > 0 && MeasureRenderedHeight(current.Append(block).ToArray(), width) > surface.ImageableHeight)
                {
                    pages.Add(current.ToArray());
                    current.Clear();
                }
                current.Add(block);
            }
        }

        if (current.Count > 0) pages.Add(current.ToArray());
        return pages.Count == 0 ? [Array.Empty<RenderedThermalReceiptBlock>()] : pages;
    }

    internal static double MeasureRenderedHeight(IEnumerable<RenderedThermalReceiptBlock> blocks, double width)
    {
        var panel = new StackPanel { Width = width };
        foreach (var block in blocks) panel.Children.Add(CreateVisual(block, width));
        panel.Measure(new Size(width, double.PositiveInfinity));
        return Math.Max(panel.DesiredSize.Height, 1);
    }

    internal static FrameworkElement CreateVisual(RenderedThermalReceiptBlock block, double width)
    {
        if (block.Total is not null)
        {
            var totalRow = new Grid
            {
                Width = width,
                Margin = new Thickness(0, block.TopMargin, 0, 0)
            };
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            totalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            AddCell(totalRow, block.Total.Label, 0, block, TextWrapping.NoWrap, TextAlignment.Left);
            AddCell(totalRow, block.Total.Amount, 2, block, TextWrapping.NoWrap, TextAlignment.Right);
            return totalRow;
        }

        if (block.Item is not null)
        {
            var row = new Grid
            {
                Width = width,
                Margin = new Thickness(0, block.TopMargin, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            AddCell(row, block.Item.QuantityText, 0, block);
            AddCell(row, block.Item.Description, 1, block, TextWrapping.NoWrap, TextAlignment.Left, TextTrimming.CharacterEllipsis);
            AddCell(row, block.Item.UnitPriceText, 2, block, TextWrapping.NoWrap, TextAlignment.Right);
            if (!string.IsNullOrWhiteSpace(block.Item.LineTotalText))
                AddCell(row, block.Item.LineTotalText, 3, block, TextWrapping.NoWrap, TextAlignment.Right);
            return row;
        }

        if (block.Option is not null)
        {
            var row = new Grid
            {
                Width = width,
                Margin = new Thickness(0, block.TopMargin, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            AddCell(row, $"- {block.Option.Description}", 1, block, TextWrapping.NoWrap, TextAlignment.Left, TextTrimming.CharacterEllipsis);
            AddCell(row, block.Option.AmountText, 2, block, TextWrapping.NoWrap, TextAlignment.Right);
            return row;
        }

        return new TextBlock
        {
            Width = width,
            FontFamily = new FontFamily(ThermalPrintLayout.ReceiptFontFamilyName),
            FontStretch = FontStretches.Normal,
            FontSize = block.FontSize,
            FontWeight = block.FontWeight,
            TextAlignment = block.Alignment,
            TextWrapping = TextWrapping.NoWrap,
            Text = block.IsIndented ? block.Text.TrimStart() : block.Text,
            Margin = block.IsIndented
                ? new Thickness(18, block.TopMargin, 0, 0)
                : new Thickness(0, block.TopMargin, 0, 0)
        };
    }

    private static void AddCell(
        Grid row,
        string text,
        int column,
        RenderedThermalReceiptBlock block,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextAlignment alignment = TextAlignment.Left,
        TextTrimming trimming = TextTrimming.None)
    {
        var cell = new TextBlock
        {
            FontFamily = new FontFamily(ThermalPrintLayout.ReceiptFontFamilyName),
            FontStretch = FontStretches.Normal,
            FontSize = block.FontSize,
            FontWeight = block.FontWeight,
            TextAlignment = alignment,
            TextWrapping = wrapping,
            TextTrimming = trimming,
            Text = text,
            Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0)
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    public static double MeasureHeight(string text, double width)
    {
        return MeasureTextHeight(text, width, FontSize);
    }

    internal static double MeasureTextHeight(string text, double width, double fontSize)
    {
        var block = CreateTextBlock(text, width, TextWrapping.Wrap, fontSize);
        block.Measure(new Size(width, double.PositiveInfinity));
        return Math.Max(block.DesiredSize.Height, 1);
    }

    internal static double MeasureTextWidth(string text, double fontSize)
    {
        var block = CreateTextBlock(text, 1, TextWrapping.NoWrap, fontSize);
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(block.DesiredSize.Width, 1);
    }

    private static List<string> WrapLine(string line, double width, double lineHeight)
    {
        if (line.Length == 0) return [string.Empty];
        var remaining = line;
        var result = new List<string>();
        while (remaining.Length > 0)
        {
            var bestLength = 0;
            for (var length = 1; length <= remaining.Length; length++)
            {
                if (MeasureHeight(remaining[..length], width) > lineHeight) break;
                bestLength = length;
            }
            if (bestLength == 0) bestLength = 1;
            if (bestLength < remaining.Length)
            {
                var cut = remaining[..bestLength].LastIndexOf(' ');
                if (cut > 0) bestLength = cut;
            }
            result.Add(remaining[..bestLength].TrimEnd());
            remaining = remaining[bestLength..].TrimStart();
        }
        return result;
    }

    private static TextBlock CreateTextBlock(string text, double width, TextWrapping wrapping, double fontSize = FontSize) => new()
    {
        Width = width,
        FontFamily = new FontFamily(ReceiptFontFamilyName),
        FontStretch = FontStretches.Normal,
        FontSize = fontSize,
        TextWrapping = wrapping,
        Text = text
    };
}

internal sealed record RenderedThermalReceiptBlock(
    string Text,
    string? AtomicGroup,
    double FontSize,
    TextAlignment Alignment,
    FontWeight FontWeight,
    bool IsIndented = false,
    PrintReceiptItem? Item = null,
    PrintReceiptOption? Option = null,
    double TopMargin = 0,
    RenderedThermalReceiptTotal? Total = null);

internal sealed record RenderedThermalReceiptTotal(string Label, string Amount);

internal static class ThermalReceiptRenderer
{
    public static IReadOnlyList<RenderedThermalReceiptBlock> Render(PrintReceiptContent content, double width)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        var isKitchen = content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Heading);
        var rendered = new List<RenderedThermalReceiptBlock>();
        PrintReceiptBlockKind? previousKind = null;
        foreach (var block in content.Blocks)
        {
            var blockRendering = RenderBlock(block, width, isKitchen);
            if (!isKitchen && block.Kind == PrintReceiptBlockKind.Ticket)
                blockRendering = AddTopMargin(blockRendering, ThermalPrintLayout.CustomerSectionGap);
            else if (!isKitchen && block.Kind == PrintReceiptBlockKind.Marker && previousKind != PrintReceiptBlockKind.Marker)
                blockRendering = AddTopMargin(blockRendering, ThermalPrintLayout.CustomerSectionGap);

            rendered.AddRange(blockRendering);
            previousKind = block.Kind;
        }

        return rendered;
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderBlock(PrintReceiptBlock block, double width, bool isKitchen)
    {
        ArgumentNullException.ThrowIfNull(block);
        var group = block.AtomicGroup;
        return block.Kind switch
        {
            PrintReceiptBlockKind.LegacyText => RenderWrapped(block.Text, width, group, BodyFontSize(isKitchen)),
            PrintReceiptBlockKind.Heading => RenderCentered(block.Text, width, group, ThermalPrintLayout.KitchenHeadingFontSize, FontWeights.Bold),
            PrintReceiptBlockKind.BusinessName => RenderCentered(block.Text, width, group, ThermalPrintLayout.CustomerBusinessNameFontSize, FontWeights.Bold),
            PrintReceiptBlockKind.Identity => RenderCentered(Combine(block), width, group, BodyFontSize(isKitchen), FontWeights.Normal),
            PrintReceiptBlockKind.LegalIdentity => RenderCentered(block.Text, width, group, ThermalPrintLayout.CustomerLegalIdentityFontSize, FontWeights.Normal),
            PrintReceiptBlockKind.Reference or
            PrintReceiptBlockKind.Timestamp or
            PrintReceiptBlockKind.Marker => RenderCentered(Combine(block), width, group, BodyFontSize(isKitchen), FontWeights.Bold),
            PrintReceiptBlockKind.Ticket => RenderWrapped(Combine(block, separator: "    "), width, group, BodyFontSize(isKitchen)),
            PrintReceiptBlockKind.Footer => RenderFooter(block, width, group),
            PrintReceiptBlockKind.Separator => [new(new string('-', CharacterCapacity(width, BodyFontSize(isKitchen))), group, BodyFontSize(isKitchen), TextAlignment.Left, FontWeights.Normal)],
            PrintReceiptBlockKind.LabelValue => RenderLabelValue(block.Text, block.SecondaryText, width, group, BodyFontSize(isKitchen)),
            PrintReceiptBlockKind.CustomerInfo => RenderCenteredLabelValue(block.Text, block.SecondaryText, width, group, ThermalPrintLayout.CustomerBodyFontSize),
            PrintReceiptBlockKind.Item => block.Item is not null && !isKitchen
                ? RenderItem(block, group)
                : RenderWrapped(Combine(block), width, group, BodyFontSize(isKitchen)),
            PrintReceiptBlockKind.Option when block.Option is not null && !isKitchen
                => RenderOption(block.Option, group),
            PrintReceiptBlockKind.Option => RenderPrefixed("    - ", Combine(block), width, group, BodyFontSize(isKitchen)),
            PrintReceiptBlockKind.ItemAmount => RenderPrefixed("  ", block.Text, width, group, ThermalPrintLayout.CustomerBodyFontSize),
            PrintReceiptBlockKind.Tax => RenderTax(block, width, group),
            PrintReceiptBlockKind.Payment or PrintReceiptBlockKind.PaymentConfirmation => RenderLabelValue(block.Text, block.SecondaryText, width, group, ThermalPrintLayout.CustomerBodyFontSize),
            PrintReceiptBlockKind.Total when block.Text.Equals("Total EUR", StringComparison.Ordinal)
                => RenderTotal(block, group),
            PrintReceiptBlockKind.Total => RenderCentered(
                Combine(block, separator: " : "),
                width,
                group,
                isKitchen ? ThermalPrintLayout.KitchenTotalFontSize : ThermalPrintLayout.CustomerTotalFontSize,
                FontWeights.Bold),
            _ => RenderWrapped(Combine(block), width, group, BodyFontSize(isKitchen))
        };
    }

    private static IEnumerable<RenderedThermalReceiptBlock> AddTopMargin(
        IEnumerable<RenderedThermalReceiptBlock> blocks,
        double topMargin)
    {
        var first = true;
        foreach (var block in blocks)
        {
            yield return first ? block with { TopMargin = topMargin } : block;
            first = false;
        }
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderItem(PrintReceiptBlock block, string? group)
    {
        var item = block.Item!;
        yield return new(
            $"{item.QuantityText} {item.Description} {item.UnitPriceText} {item.LineTotalText}",
            group,
            ThermalPrintLayout.CustomerBodyFontSize,
            TextAlignment.Left,
            FontWeights.Normal,
            Item: item);
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderOption(PrintReceiptOption option, string? group)
    {
        yield return new(
            $"    - {option.Description} {option.AmountText}".TrimEnd(),
            group,
            ThermalPrintLayout.CustomerBodyFontSize,
            TextAlignment.Left,
            FontWeights.Normal,
            IsIndented: true,
            Option: option);
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderFooter(PrintReceiptBlock block, double width, string? group)
    {
        foreach (var rendered in RenderCentered(block.Text, width, group, ThermalPrintLayout.CustomerBodyFontSize, FontWeights.Normal))
            yield return block.Text.Equals("Merci de votre visite !", StringComparison.Ordinal)
                ? rendered with { TopMargin = ThermalPrintLayout.CustomerFooterGap }
                : rendered;
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderTotal(PrintReceiptBlock block, string? group)
    {
        var amount = string.IsNullOrWhiteSpace(block.SecondaryText) ? string.Empty : $"EUR {block.SecondaryText.Trim()}";
        yield return new(
            $"{block.Text} {block.SecondaryText}".Trim(),
            group,
            ThermalPrintLayout.CustomerTotalFontSize,
            TextAlignment.Left,
            FontWeights.Bold,
            Total: new RenderedThermalReceiptTotal("Total", amount));
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderCentered(string text, double width, string? group, double fontSize, FontWeight fontWeight)
    {
        foreach (var line in WrapLine(text, width, fontSize))
            yield return new(line, group, fontSize, TextAlignment.Center, fontWeight);
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderLabelValue(string label, string? value, double width, string? group, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        var prefix = $"{label} : ";
        var first = true;
        foreach (var line in WrapLine(prefix + value.Trim(), width, fontSize))
        {
            yield return new(first ? line : new string(' ', prefix.Length) + line, group, fontSize, TextAlignment.Left, FontWeights.Normal);
            first = false;
        }
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderCenteredLabelValue(string label, string? value, double width, string? group, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (var line in WrapLine($"{label} : {value.Trim()}", width, fontSize))
            yield return new(line, group, fontSize, TextAlignment.Center, FontWeights.Normal);
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderTax(PrintReceiptBlock block, double width, string? group)
    {
        var text = block.Text.Equals("Total HT", StringComparison.Ordinal)
            ? Combine(block, separator: " : ")
            : $"{block.Text} : {block.SecondaryText} ({block.TertiaryText})";
        foreach (var line in WrapLine(text, width, ThermalPrintLayout.CustomerBodyFontSize))
            yield return new(line, group, ThermalPrintLayout.CustomerBodyFontSize, TextAlignment.Left, FontWeights.Normal);
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderPrefixed(string prefix, string text, double width, string? group, double fontSize)
    {
        var first = true;
        foreach (var line in WrapLine(prefix + text.Trim(), width, fontSize))
        {
            yield return new(first ? line : new string(' ', prefix.Length) + line, group, fontSize, TextAlignment.Left, FontWeights.Normal, IsIndented: true);
            first = false;
        }
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderWrapped(string text, double width, string? group, double fontSize)
    {
        foreach (var line in WrapLine(text, width, fontSize))
            yield return new(line, group, fontSize, TextAlignment.Left, FontWeights.Normal);
    }

    private static string Combine(PrintReceiptBlock block, string separator = " ") =>
        string.Join(separator, new[] { block.Text, block.SecondaryText, block.TertiaryText }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));

    private static List<string> WrapLine(string line, double width, double fontSize)
    {
        if (line.Length == 0) return [string.Empty];
        var lineHeight = ThermalPrintLayout.MeasureTextHeight("M", width, fontSize);
        var result = new List<string>();
        var remaining = line.Trim();
        while (remaining.Length > 0)
        {
            var bestLength = 0;
            for (var length = 1; length <= remaining.Length; length++)
            {
                if (ThermalPrintLayout.MeasureTextHeight(remaining[..length], width, fontSize) > lineHeight) break;
                bestLength = length;
            }
            if (bestLength == 0) bestLength = 1;
            if (bestLength < remaining.Length)
            {
                var cut = remaining[..bestLength].LastIndexOf(' ');
                if (cut > 0) bestLength = cut;
            }
            result.Add(remaining[..bestLength].TrimEnd());
            remaining = remaining[bestLength..].TrimStart();
        }
        return result;
    }

    private static double BodyFontSize(bool isKitchen) => isKitchen ? ThermalPrintLayout.KitchenBodyFontSize : ThermalPrintLayout.CustomerBodyFontSize;

    private static int CharacterCapacity(double width, double fontSize)
    {
        var characterWidth = Math.Max(1, ThermalPrintLayout.MeasureTextWidth("M", fontSize));
        return Math.Max(1, (int)Math.Floor(width / characterWidth));
    }
}

internal static class StaPrintThread
{
    public static Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Sushi81 POS Windows print STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

public sealed class WindowsOrderPrintDispatcher(
    IBusinessSettingsStore settings,
    ILocalConfigurationService configuration,
    IPrintDocumentSubmitter submitter,
    IBusinessClock clock) : IOrderPrintOutcomeDispatcher
{
    private readonly IBusinessSettingsStore settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILocalConfigurationService configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly IPrintDocumentSubmitter submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));
    private readonly OrderPrintDocumentFactory factory = new(clock ?? throw new ArgumentNullException(nameof(clock)));

    public async Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) =>
        _ = await DispatchInitialAsync(committedOrder, PrintIntent.InitialAutomatic, cancellationToken);

    public async Task<PrintDispatchResult> DispatchInitialAsync(OrderSnapshot committedOrder, PrintIntent intent = PrintIntent.InitialAutomatic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committedOrder);
        var results = new List<PrintDocumentResult>(2);
        foreach (var kind in new[] { PrintDocumentKind.Kitchen, PrintDocumentKind.Customer })
            results.Add(await PrintDocumentAsync(committedOrder, kind, intent, cancellationToken));
        return new(results);
    }

    public async Task<PrintDocumentResult> PrintDocumentAsync(OrderSnapshot committedOrder, PrintDocumentKind kind, PrintIntent intent = PrintIntent.ExplicitReprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(committedOrder);
        OrderPrintDocument document;
        try
        {
            var businessSettings = await settings.GetAsync(cancellationToken);
            document = factory.Create(committedOrder, businessSettings.ReceiptIdentity, kind, intent);
        }
        catch (OperationCanceledException)
        {
            return new(kind, PrintOutcomeStatus.GenerationFailed, SafeMessage(kind, PrintOutcomeStatus.GenerationFailed));
        }
        catch (Exception)
        {
            return new(kind, PrintOutcomeStatus.GenerationFailed, SafeMessage(kind, PrintOutcomeStatus.GenerationFailed));
        }

        try
        {
            var local = await configuration.LoadAsync(cancellationToken);
            var status = kind == PrintDocumentKind.Kitchen
                ? await submitter.SubmitAsync(document, local.KitchenPrinterQueueId, local.KitchenPrinterQueueName, cancellationToken)
                : await submitter.SubmitAsync(document, local.CustomerPrinterQueueId, local.CustomerPrinterQueueName, cancellationToken);
            return status == PrintOutcomeStatus.Succeeded
                ? PrintDocumentResult.Success(document)
                : new(kind, status, SafeMessage(kind, status), document);
        }
        catch (OperationCanceledException)
        {
            return new(kind, PrintOutcomeStatus.SubmissionFailed, SafeMessage(kind, PrintOutcomeStatus.SubmissionFailed), document);
        }
        catch (Exception)
        {
            return new(kind, PrintOutcomeStatus.SubmissionFailed, SafeMessage(kind, PrintOutcomeStatus.SubmissionFailed), document);
        }
    }

    private static string SafeMessage(PrintDocumentKind kind, PrintOutcomeStatus status)
    {
        var label = kind == PrintDocumentKind.Kitchen ? "kitchen" : "customer";
        return status switch
        {
            PrintOutcomeStatus.QueueUnavailable => $"The {label} printer is not configured or is unavailable.",
            PrintOutcomeStatus.GenerationFailed => $"The {label} document could not be generated from the committed order.",
            PrintOutcomeStatus.AmbiguousSubmission => $"The {label} print result is uncertain. Use explicit reprint if another copy is required.",
            _ => $"The {label} document could not be submitted to the printer."
        };
    }
}
