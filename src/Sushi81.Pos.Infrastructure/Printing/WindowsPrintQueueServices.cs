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
        var pages = ThermalPrintLayout.Paginate(document.Content, surface);
        var fixedDocument = new FixedDocument();
        foreach (var pageText in pages)
        {
            var contentHeight = ThermalPrintLayout.MeasureHeight(pageText, surface.ImageableWidth);
            var bottomMargin = Math.Max(0, surface.PageHeight - surface.OriginHeight - surface.ImageableHeight);
            var pageHeight = Math.Min(surface.PageHeight, surface.OriginHeight + contentHeight + bottomMargin);
            var page = new FixedPage { Width = surface.PageWidth, Height = pageHeight };
            var text = new TextBlock
            {
                Width = surface.ImageableWidth,
                Margin = new Thickness(surface.OriginWidth, surface.OriginHeight, 0, 0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = ThermalPrintLayout.FontSize,
                TextWrapping = TextWrapping.NoWrap,
                Text = pageText
            };
            page.Children.Add(text);
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
    public const double FontSize = 9;
    public const double FallbackPageWidth = 288;
    public const double FallbackPageHeight = 1440;
    public const double MinimumFallbackPageWidth = 72;
    public const double MaximumFallbackPageWidth = 576;
    public const double MinimumFallbackPageHeight = 144;
    public const double MaximumFallbackPageHeight = 1440;
    private const double FallbackMargin = 9;

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
        var lineHeight = MeasureHeight("M", surface.ImageableWidth);
        var wrappedLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .SelectMany(line => WrapLine(line, surface.ImageableWidth, lineHeight))
            .ToArray();
        var pages = new List<string>();
        var current = new List<string>();
        foreach (var line in wrappedLines)
        {
            var candidate = string.Join(Environment.NewLine, current.Append(line));
            if (current.Count > 0 && MeasureHeight(candidate, surface.ImageableWidth) > surface.ImageableHeight)
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

        var renderedBlocks = ThermalReceiptRenderer.Render(content, surface.ImageableWidth);
        var units = new List<string>();
        string? previousAtomicGroup = null;
        foreach (var renderedBlock in renderedBlocks)
        {
            if (renderedBlock.AtomicGroup is not null
                && units.Count > 0
                && renderedBlock.AtomicGroup == previousAtomicGroup)
            {
                units[^1] = string.Join(Environment.NewLine, units[^1], renderedBlock.Text);
            }
            else
            {
                units.Add(renderedBlock.Text);
            }
            previousAtomicGroup = renderedBlock.AtomicGroup;
        }

        var pages = new List<string>();
        var current = new List<string>();
        foreach (var unit in units)
        {
            var candidate = string.Join(Environment.NewLine, current.Append(unit));
            if (current.Count > 0 && MeasureHeight(candidate, surface.ImageableWidth) > surface.ImageableHeight)
            {
                pages.Add(string.Join(Environment.NewLine, current));
                current.Clear();
            }

            if (MeasureHeight(unit, surface.ImageableWidth) <= surface.ImageableHeight)
            {
                current.Add(unit);
                continue;
            }

            foreach (var line in unit.Split(Environment.NewLine, StringSplitOptions.None))
            {
                candidate = string.Join(Environment.NewLine, current.Append(line));
                if (current.Count > 0 && MeasureHeight(candidate, surface.ImageableWidth) > surface.ImageableHeight)
                {
                    pages.Add(string.Join(Environment.NewLine, current));
                    current.Clear();
                }
                current.Add(line);
            }
        }

        if (current.Count > 0) pages.Add(string.Join(Environment.NewLine, current));
        return pages.Count == 0 ? [string.Empty] : pages;
    }

    public static double MeasureHeight(string text, double width)
    {
        var block = CreateTextBlock(text, width, TextWrapping.Wrap);
        block.Measure(new Size(width, double.PositiveInfinity));
        return Math.Max(block.DesiredSize.Height, 1);
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

    private static TextBlock CreateTextBlock(string text, double width, TextWrapping wrapping) => new()
    {
        Width = width,
        FontFamily = new FontFamily("Consolas"),
        FontSize = FontSize,
        TextWrapping = wrapping,
        Text = text
    };
}

internal sealed record RenderedThermalReceiptBlock(string Text, string? AtomicGroup);

internal static class ThermalReceiptRenderer
{
    public static IReadOnlyList<RenderedThermalReceiptBlock> Render(PrintReceiptContent content, double width)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        return content.Blocks
            .SelectMany(block => RenderBlock(block, width))
            .ToArray();
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderBlock(PrintReceiptBlock block, double width)
    {
        ArgumentNullException.ThrowIfNull(block);
        var group = block.AtomicGroup;
        return block.Kind switch
        {
            PrintReceiptBlockKind.LegacyText => RenderLegacy(block.Text, width, group),
            PrintReceiptBlockKind.Heading or
            PrintReceiptBlockKind.Identity or
            PrintReceiptBlockKind.Reference or
            PrintReceiptBlockKind.Timestamp or
            PrintReceiptBlockKind.Marker or
            PrintReceiptBlockKind.Footer => RenderCentered(Combine(block), width, group),
            PrintReceiptBlockKind.Separator => [new(new string('-', CharacterCapacity(width)), group)],
            PrintReceiptBlockKind.LabelValue => RenderLabelValue(block.Text, block.SecondaryText, width, group),
            PrintReceiptBlockKind.Item => RenderWrapped(Combine(block), width, group),
            PrintReceiptBlockKind.Option => RenderWrapped($"  - {Combine(block)}", width, group),
            PrintReceiptBlockKind.ItemAmount => RenderWrapped($"  {block.Text}", width, group),
            PrintReceiptBlockKind.Tax or PrintReceiptBlockKind.Payment => RenderLabelValue(block.Text, block.SecondaryText, width, group),
            PrintReceiptBlockKind.Total => RenderCentered(
                block.Text.Equals("Total EUR", StringComparison.Ordinal)
                    ? Combine(block, separator: " ")
                    : Combine(block, separator: " : "),
                width,
                group),
            _ => RenderWrapped(Combine(block), width, group)
        };
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderLegacy(string text, double width, string? group)
    {
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            foreach (var wrapped in WrapLine(line, width))
                yield return new(wrapped, group);
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderCentered(string text, double width, string? group)
    {
        foreach (var line in WrapLine(text, width))
        {
            var measuredWidth = MeasureTextWidth(line);
            var leftPadding = Math.Max(0, (width - measuredWidth) / 2);
            yield return new(new string(' ', (int)Math.Floor(leftPadding / Math.Max(1, MeasureTextWidth("M")))) + line, group);
        }
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderLabelValue(string label, string? value, double width, string? group)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        var prefix = $"{label} : ";
        var first = true;
        foreach (var line in WrapLine(prefix + value.Trim(), width))
        {
            yield return new(first ? line : new string(' ', prefix.Length) + line, group);
            first = false;
        }
    }

    private static IEnumerable<RenderedThermalReceiptBlock> RenderWrapped(string text, double width, string? group)
    {
        foreach (var line in WrapLine(text, width)) yield return new(line, group);
    }

    private static string Combine(PrintReceiptBlock block, string separator = " ") =>
        string.Join(separator, new[] { block.Text, block.SecondaryText, block.TertiaryText }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));

    private static List<string> WrapLine(string line, double width)
    {
        if (line.Length == 0) return [string.Empty];
        var lineHeight = ThermalPrintLayout.MeasureHeight("M", width);
        var result = new List<string>();
        var remaining = line.Trim();
        while (remaining.Length > 0)
        {
            var bestLength = 0;
            for (var length = 1; length <= remaining.Length; length++)
            {
                if (ThermalPrintLayout.MeasureHeight(remaining[..length], width) > lineHeight) break;
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

    private static int CharacterCapacity(double width)
    {
        var characterWidth = Math.Max(1, MeasureTextWidth("M"));
        return Math.Max(1, (int)Math.Floor(width / characterWidth));
    }

    private static double MeasureTextWidth(string text)
    {
        var block = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = ThermalPrintLayout.FontSize,
            TextWrapping = TextWrapping.NoWrap,
            Text = text
        };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(block.DesiredSize.Width, 1);
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
