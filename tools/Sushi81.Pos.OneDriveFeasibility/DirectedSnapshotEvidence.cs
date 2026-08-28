using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record DirectedSnapshotEvidence(
    DirectedTransferIdentity Transfer,
    string SnapshotPath,
    string SnapshotChecksum,
    long SnapshotByteLength,
    bool IntegrityConfirmed,
    bool SyncConfirmed)
{
    public bool IsValid => Transfer.IsValid
        && !string.IsNullOrWhiteSpace(SnapshotPath)
        && SnapshotChecksum is { Length: 64 } checksum
        && checksum.All(Uri.IsHexDigit)
        && SnapshotByteLength > 0
        && IntegrityConfirmed
        && SyncConfirmed;

    public static async Task<DirectedSnapshotEvidence> CaptureAsync(
        DirectedTransferIdentity transfer,
        string snapshotPath,
        bool syncConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!transfer.IsValid || !File.Exists(snapshotPath))
        {
            return new(transfer, snapshotPath, string.Empty, 0, false, syncConfirmed);
        }

        var length = new FileInfo(snapshotPath).Length;
        if (length < 1)
        {
            return new(transfer, snapshotPath, string.Empty, length, false, syncConfirmed);
        }

        var checksum = await ComputeSha256Async(snapshotPath, cancellationToken);
        var integrityConfirmed = false;
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = snapshotPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var integrity = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            integrityConfirmed = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            integrityConfirmed = false;
        }

        return new(transfer, Path.GetFullPath(snapshotPath), checksum, length, integrityConfirmed, syncConfirmed);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
