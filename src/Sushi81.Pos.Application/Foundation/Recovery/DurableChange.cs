namespace Sushi81.Pos.Application.Foundation.Recovery;

public sealed record DurableChange(long Sequence, DateTimeOffset CommittedAtUtc);
