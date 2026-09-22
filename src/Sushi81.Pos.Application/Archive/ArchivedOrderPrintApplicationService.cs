using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Archive;

/// <summary>
/// Explicitly reprints an order snapshot that has already been hydrated from a selected
/// annual archive. It deliberately has no live-order, catalogue, or write-authority dependency.
/// </summary>
public interface IArchivedOrderPrintApplicationService
{
    Task<PrintDocumentResult> ReprintAsync(
        OrderSnapshot archivedSnapshot,
        PrintDocumentKind kind,
        CancellationToken cancellationToken = default);
}

public sealed class ArchivedOrderPrintApplicationService(IOrderPrintOutcomeDispatcher dispatcher)
    : IArchivedOrderPrintApplicationService
{
    private readonly IOrderPrintOutcomeDispatcher dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public Task<PrintDocumentResult> ReprintAsync(
        OrderSnapshot archivedSnapshot,
        PrintDocumentKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archivedSnapshot);
        return dispatcher.PrintDocumentAsync(archivedSnapshot, kind, PrintIntent.ExplicitReprint, cancellationToken);
    }
}
