namespace Sushi81.Pos.Application.Foundation.Recovery;

/// <summary>Reads the canonical local-first monotonic business-data revision.</summary>
public interface IBusinessRevisionReader
{
    Task<long> ReadAsync(CancellationToken cancellationToken = default);
}
