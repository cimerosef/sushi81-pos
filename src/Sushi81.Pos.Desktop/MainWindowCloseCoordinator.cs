using System.ComponentModel;

namespace Sushi81.Pos.Desktop;

public enum MainWindowCloseIntent
{
    Retain,
    Transfer,
    Cancel
}

public readonly record struct MainWindowCloseRequest(MainWindowCloseIntent Intent, Guid? TargetDeviceId = null);

/// <summary>
/// The single close arbiter for the desktop window. It owns the cancel/continue
/// handshake so a second Closing subscription cannot race recovery disposal or a
/// target-directed handoff.
/// </summary>
public sealed class MainWindowCloseCoordinator
{
    private readonly Func<bool> isAuthoritative;
    private readonly Func<Task<MainWindowCloseRequest>> requestAsync;
    private readonly Func<Guid, Task<bool>> transferAsync;
    private readonly Func<ValueTask>? flushAsync;
    private readonly Action requestFinalClose;
    private readonly Action<Exception> reportFailure;
    private int closeStarted;
    private int finalCloseAllowed;

    public MainWindowCloseCoordinator(
        Func<bool> isAuthoritative,
        Func<Task<MainWindowCloseRequest>> requestAsync,
        Func<Guid, Task<bool>> transferAsync,
        Func<ValueTask>? flushAsync,
        Action requestFinalClose,
        Action<Exception> reportFailure)
    {
        this.isAuthoritative = isAuthoritative ?? throw new ArgumentNullException(nameof(isAuthoritative));
        this.requestAsync = requestAsync ?? throw new ArgumentNullException(nameof(requestAsync));
        this.transferAsync = transferAsync ?? throw new ArgumentNullException(nameof(transferAsync));
        this.flushAsync = flushAsync;
        this.requestFinalClose = requestFinalClose ?? throw new ArgumentNullException(nameof(requestFinalClose));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    public bool IsFinalCloseAllowed => Volatile.Read(ref finalCloseAllowed) != 0;

    public bool IsCloseInProgress => Volatile.Read(ref closeStarted) != 0;

    public async Task HandleClosingAsync(CancelEventArgs closing)
    {
        ArgumentNullException.ThrowIfNull(closing);
        if (IsFinalCloseAllowed)
            return;

        // Every first close request is cancelled while the asynchronous decision and
        // durable cleanup run. Repeated title-bar/Alt+F4 requests remain harmless.
        closing.Cancel = true;
        if (Interlocked.Exchange(ref closeStarted, 1) != 0)
            return;

        try
        {
            MainWindowCloseRequest request;
            if (isAuthoritative())
            {
                request = await requestAsync();
                if (request.Intent == MainWindowCloseIntent.Cancel)
                    return;

                if (request.Intent == MainWindowCloseIntent.Transfer)
                {
                    if (request.TargetDeviceId is not { } targetDeviceId)
                        throw new InvalidOperationException("A target-directed handoff requires an exact target device.");

                    if (!await transferAsync(targetDeviceId))
                        return;
                }
            }

            if (flushAsync is not null)
            {
                try
                {
                    await flushAsync();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Preserve the existing M06 shutdown contract: surface and log the
                    // recovery failure, but do not mutate authority or run a second close.
                    reportFailure(exception);
                }
            }

            Volatile.Write(ref finalCloseAllowed, 1);
            requestFinalClose();
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a failed close request; leave the authoritative window open.
        }
        catch (Exception exception)
        {
            reportFailure(exception);
        }
        finally
        {
            if (!IsFinalCloseAllowed)
                Volatile.Write(ref closeStarted, 0);
        }
    }
}
