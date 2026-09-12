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
        Task.Run<IReadOnlyList<PrintQueueInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var server = new LocalPrintServer();
            var queues = server.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections,
                EnumeratedPrintQueueTypes.Shared
            });
            var result = new List<PrintQueueInfo>();
            foreach (var queue in queues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(new(queue.FullName, queue.Name));
                queue.Dispose();
            }
            return (IReadOnlyList<PrintQueueInfo>)result.OrderBy(queue => queue.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
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
        return await Task.Run(() => SubmitCore(document, configuredQueueId, configuredQueueName, cancellationToken), cancellationToken);
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
            foreach (var candidate in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections, EnumeratedPrintQueueTypes.Shared }))
            {
                if (string.Equals(candidate.FullName, configuredQueueId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Name, configuredQueueName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.FullName, configuredQueueName, StringComparison.OrdinalIgnoreCase))
                {
                    queue = candidate;
                    break;
                }

                candidate.Dispose();
            }

            if (queue is null) return PrintOutcomeStatus.QueueUnavailable;

            var documentPaginator = CreateDocumentPaginator(document);
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

    private static FixedDocument CreateDocumentPaginator(OrderPrintDocument document)
    {
        const double pageWidth = 288;
        const double pageHeight = 7200;
        var fixedDocument = new FixedDocument();
        var page = new FixedPage { Width = pageWidth, Height = pageHeight };
        var text = new TextBlock
        {
            Width = pageWidth - 18,
            Margin = new Thickness(9, 9, 9, 9),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            Text = document.Text
        };
        page.Children.Add(text);
        var pageContent = new PageContent();
        ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
        fixedDocument.Pages.Add(pageContent);
        return fixedDocument;
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
