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

    public static async Task CreateSyntheticAsync(string snapshotPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            throw new ArgumentException("A synthetic snapshot path is required.", nameof(snapshotPath));
        }

        var fullPath = Path.GetFullPath(snapshotPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
        {
            throw new IOException("Synthetic snapshot path already exists; refusing to overwrite it.");
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE synthetic_snapshot (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO synthetic_snapshot(value) VALUES ('M02 synthetic snapshot payload');";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
