using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>
/// Independent changed-only scheduler for recovery checkpoints. Its local watermark is
/// separate from the M06 local-recovery debounce state and never controls authority.
/// </summary>
public sealed class OneDriveRecoveryCheckpointScheduler : IRecoveryCheckpointScheduler, IAsyncDisposable
{
    private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IRecoveryCheckpointPublisher publisher;
    private readonly IBusinessClock clock;
    private readonly string watermarkPath;
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private readonly object stateLock = new();
    private Timer? dueTimer;
    private DurableChange? pending;
    private RecoveryCheckpointWatermark? completed;
    private bool disposed;

    public OneDriveRecoveryCheckpointScheduler(
        IRecoveryCheckpointPublisher publisher,
        string watermarkPath,
        IBusinessClock clock)
    {
        this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.watermarkPath = Path.GetFullPath(watermarkPath ?? throw new ArgumentNullException(nameof(watermarkPath)));
        completed = LoadWatermark(this.watermarkPath);
    }

    public void NotifyCommitted(DurableChange change)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentOutOfRangeException.ThrowIfNegative(change.Sequence);

        lock (stateLock)
        {
            if (completed is { } watermark && change.Sequence <= watermark.BusinessRevision)
                return;
            if (pending is null || change.Sequence > pending.Sequence)
                pending = change;
            dueTimer ??= new Timer(static state =>
            {
                var scheduler = (OneDriveRecoveryCheckpointScheduler)state!;
                _ = scheduler.RunDuePublicationAsync();
            }, this, NormalInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<bool> TryPublishDueAsync(bool force, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            DurableChange? change;
            lock (stateLock)
            {
                change = pending is { } candidate
                    && (completed is null || candidate.Sequence > completed.BusinessRevision)
                    && (force || completed is null || clock.UtcNow - completed.CompletedAtUtc >= NormalInterval)
                    ? candidate
                    : null;
            }

            if (change is null)
                return false;

            RecoveryCheckpointPublicationResult result;
            try
            {
                result = await publisher.PublishAsync(change.Sequence, cancellationToken);
                result.Metadata.Validate();
                if (result.Metadata.BusinessRevision != change.Sequence)
                    throw new InvalidDataException("The checkpoint publisher returned a mismatched business revision.");

                var watermark = new RecoveryCheckpointWatermark(1, change.Sequence, result.Metadata.CheckpointId, result.Metadata.CreatedAtUtc);
                watermark.Validate();
                await PersistWatermarkAsync(watermark, cancellationToken);
                lock (stateLock)
                {
                    completed = watermark;
                    if (pending?.Sequence == change.Sequence)
                        pending = null;
                }
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // A cloud failure is retryable. It never changes authority or blocks a
                // successful local business commit, and the pending change is retained.
                return false;
            }
        }
        finally
        {
            publishGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            dueTimer?.Dispose();
            dueTimer = null;
            publishGate.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private async Task RunDuePublicationAsync()
    {
        lock (stateLock)
        {
            dueTimer?.Dispose();
            dueTimer = null;
        }

        try
        {
            await TryPublishDueAsync(force: false);
        }
        finally
        {
            lock (stateLock)
            {
                if (!disposed && pending is not null && (completed is null || pending.Sequence > completed.BusinessRevision))
                {
                    dueTimer ??= new Timer(static state =>
                    {
                        var scheduler = (OneDriveRecoveryCheckpointScheduler)state!;
                        _ = scheduler.RunDuePublicationAsync();
                    }, this, NormalInterval, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private static RecoveryCheckpointWatermark? LoadWatermark(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var watermark = JsonSerializer.Deserialize<RecoveryCheckpointWatermark>(File.ReadAllText(path), JsonOptions);
            watermark?.Validate();
            return watermark;
        }
        catch
        {
            // A malformed scheduler watermark is not authority evidence. Starting with
            // no completed cloud checkpoint is safe; the next validated publication
            // replaces it with a canonical record.
            return null;
        }
    }

    private async Task PersistWatermarkAsync(RecoveryCheckpointWatermark watermark, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(watermarkPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("The checkpoint watermark path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(watermarkPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, watermark, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, watermarkPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
