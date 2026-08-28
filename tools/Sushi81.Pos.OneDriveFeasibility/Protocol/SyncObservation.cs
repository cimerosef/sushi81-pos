namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

/// <summary>
/// Deliberately conservative interpretation of a Cloud Files observation. Only
/// InSync is usable for a narrow local publication step; all other values are
/// fail-closed and never imply write authority.
/// </summary>
public enum SimulatedSyncState
{
    InSync,
    Pending,
    Partial,
    Invalid,
    Unknown,
    Error,
    NotCloud
}

public static class SyncObservation
{
    public static bool IsConfirmedInSync(SimulatedSyncState state) => state == SimulatedSyncState.InSync;

    public static bool IsFailClosed(SimulatedSyncState state) => state != SimulatedSyncState.InSync;
}
