using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>Allocates a monotonic local sequence and forwards committed changes to the single-flight scheduler.</summary>
public sealed partial class DurableChangeNotifier : IDurableChangeNotifier, IDisposable
{
    private const string SequenceFileName = "recovery-sequence.json";
    private readonly IAppPaths paths;
    private readonly IBusinessClock clock;
    private readonly IRecoveryScheduler scheduler;
    private readonly ILogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private long sequence;

    private DurableChangeNotifier(IAppPaths paths, IBusinessClock clock, IRecoveryScheduler scheduler, ILogger logger)
    {
        this.paths = paths;
        this.clock = clock;
        this.scheduler = scheduler;
        this.logger = logger;
    }

    public static async Task<DurableChangeNotifier> CreateAsync(IAppPaths paths, IBusinessClock clock, IRecoveryScheduler scheduler, ILogger logger)
    {
        var notifier = new DurableChangeNotifier(paths, clock, scheduler, logger);
        notifier.sequence = await notifier.LoadSequenceAsync();
        return notifier;
    }

    public async Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var next = checked(sequence + 1);
            try
            {
                await PersistSequenceAsync(next, cancellationToken);
                sequence = next;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The business transaction has already committed. Keep an in-memory monotonic sequence and
                // continue scheduling. On restart, validated recovery metadata reconciles the sequence.
                sequence = next;
                LogSequencePersistenceFailure(logger, exception);
            }

            try
            {
                scheduler.NotifyCommitted(new DurableChange(sequence, clock.UtcNow));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSchedulingFailure(logger, exception, sequence);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<long> LoadSequenceAsync()
    {
        var persisted = 0L;
        try
        {
            paths.EnsureInitialized();
            var path = Path.Combine(paths.ConfigDirectory, SequenceFileName);
            if (File.Exists(path))
            {
                var value = JsonSerializer.Deserialize<SequenceDocument>(File.ReadAllText(path));
                persisted = value is { Sequence: >= 0 } ? value.Sequence : 0;
            }
        }
        catch (Exception exception)
        {
            LogSequenceLoadFailure(logger, exception);
        }

        try
        {
            var highestValidatedRecoverySequence = await SqliteLocalRecoverySnapshotService
                .GetHighestValidatedSequenceAsync(paths);
            return Math.Max(persisted, highestValidatedRecoverySequence);
        }
        catch (Exception exception)
        {
            LogRecoverySequenceReconciliationFailure(logger, exception);
            return persisted;
        }
    }

    private async Task PersistSequenceAsync(long value, CancellationToken cancellationToken)
    {
        paths.EnsureInitialized();
        var path = Path.Combine(paths.ConfigDirectory, SequenceFileName);
        var temporaryPath = Path.Combine(paths.ConfigDirectory, $".{SequenceFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, new SequenceDocument(value), SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed record SequenceDocument(long Sequence);

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Dispose() => gate.Dispose();

    [LoggerMessage(EventId = 1210, Level = LogLevel.Error, Message = "Recovery sequence persistence failed after a durable business commit.")]
    private static partial void LogSequencePersistenceFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Error, Message = "Recovery scheduling failed after durable change sequence {Sequence}.")]
    private static partial void LogSchedulingFailure(ILogger logger, Exception exception, long sequence);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Error, Message = "Recovery sequence could not be loaded; starting from zero for this process.")]
    private static partial void LogSequenceLoadFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1213, Level = LogLevel.Error, Message = "Validated local recovery sequence could not be reconciled at startup.")]
    private static partial void LogRecoverySequenceReconciliationFailure(ILogger logger, Exception exception);
}
