using System.ComponentModel;

namespace Sushi81.Pos.Desktop;

/// <summary>
/// Coordinates a WPF close without synchronously waiting on asynchronous recovery work.
/// The first close is held until the async completion has finished; the continuation then
/// requests the close again, which is allowed through after completion.
/// </summary>
public sealed class AsyncCloseCoordinator
{
    private readonly Func<ValueTask> completeAsync;
    private readonly Action continueClose;
    private readonly Action<Exception>? reportFailure;
    private int closeStarted;
    private int closeCompleted;

    public AsyncCloseCoordinator(
        Func<ValueTask> completeAsync,
        Action continueClose,
        Action<Exception>? reportFailure = null)
    {
        this.completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
        this.continueClose = continueClose ?? throw new ArgumentNullException(nameof(continueClose));
        this.reportFailure = reportFailure;
    }

    public bool IsComplete => Volatile.Read(ref closeCompleted) != 0;

    public async Task HandleClosingAsync(CancelEventArgs closing)
    {
        ArgumentNullException.ThrowIfNull(closing);

        if (IsComplete)
        {
            return;
        }

        closing.Cancel = true;
        if (Interlocked.Exchange(ref closeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await completeAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reportFailure?.Invoke(exception);
        }
        finally
        {
            Volatile.Write(ref closeCompleted, 1);
            try
            {
                continueClose();
            }
            catch (Exception exception)
            {
                reportFailure?.Invoke(exception);
            }
        }
    }
}
