using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Foundation.Recovery;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>Single-flight three-second recovery scheduling driven only by successful durable commits.</summary>
public sealed partial class DebouncedRecoveryScheduler : IRecoveryScheduler, IAsyncDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(3);
    private readonly ILocalRecoverySnapshotService snapshotService;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly SemaphoreSlim snapshotGate = new(1, 1);
    private readonly object stateLock = new();
    private CancellationTokenSource? debounceCancellation;
    private DurableChange? pendingChange;
    private long lastSnapshottedSequence = -1;
    private Task scheduledWork = Task.CompletedTask;
    private bool disposed;

    public DebouncedRecoveryScheduler(
        ILocalRecoverySnapshotService snapshotService,
        TimeProvider timeProvider,
        ILogger logger)
    {
        this.snapshotService = snapshotService;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public void NotifyCommitted(DurableChange change)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(change);

        lock (stateLock)
        {
            if (change.Sequence <= lastSnapshottedSequence || (pendingChange is not null && change.Sequence <= pendingChange.Sequence))
            {
                return;
            }

            pendingChange = change;
            debounceCancellation?.Cancel();
            debounceCancellation?.Dispose();
            debounceCancellation = new CancellationTokenSource();
            scheduledWork = RunAfterDebounceAsync(debounceCancellation.Token);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Task work;
        lock (stateLock)
        {
            debounceCancellation?.Cancel();
            work = scheduledWork;
        }

        try
        {
            await work;
        }
        catch (OperationCanceledException)
        {
            // A cancellation is expected when replacing a debounce interval or flushing shutdown work.
        }

        // A durable commit may arrive while the first snapshot is active. Keep flushing until
        // the latest pending sequence is represented; the active snapshot itself is never cancelled.
        while (true)
        {
            await CreatePendingSnapshotAsync(cancellationToken);
            lock (stateLock)
            {
                if (pendingChange is null || pendingChange.Sequence <= lastSnapshottedSequence) return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            await FlushAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The failed creation has already been logged with its durable sequence. Shutdown must still release resources.
        }
        finally
        {
            disposed = true;
            debounceCancellation?.Dispose();
            snapshotGate.Dispose();
        }
    }

    private async Task RunAfterDebounceAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(DebounceInterval, timeProvider, cancellationToken);
        await CreatePendingSnapshotAsync(cancellationToken);
    }

    private async Task CreatePendingSnapshotAsync(CancellationToken cancellationToken)
    {
        await snapshotGate.WaitAsync(cancellationToken);
        try
        {
            DurableChange? change;
            lock (stateLock)
            {
                change = pendingChange is { Sequence: var sequence } pending && sequence > lastSnapshottedSequence ? pending : null;
            }

            if (change is null)
            {
                return;
            }

            try
            {
                await snapshotService.CreateAsync(change, cancellationToken);
                lock (stateLock)
                {
                    lastSnapshottedSequence = change.Sequence;
                    if (pendingChange?.Sequence == change.Sequence)
                    {
                        pendingChange = null;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSnapshotFailure(logger, exception, change.Sequence);
                throw;
            }
        }
        finally
        {
            snapshotGate.Release();
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Local recovery snapshot failed for durable change sequence {DurableChangeSequence}.")]
    private static partial void LogSnapshotFailure(ILogger logger, Exception exception, long durableChangeSequence);
}
