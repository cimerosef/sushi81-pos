namespace Sushi81.Pos.Infrastructure.Recovery;

public sealed class RecoverySnapshotValidationException(string message) : IOException(message);
