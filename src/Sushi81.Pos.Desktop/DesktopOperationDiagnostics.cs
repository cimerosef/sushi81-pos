using System.IO;
using Microsoft.Extensions.Logging;

namespace Sushi81.Pos.Desktop;

/// <summary>Failure-safe technical diagnostics for unexpected Desktop operations.</summary>
public sealed class DesktopOperationDiagnostics
{
    private static readonly Action<ILogger, string, string, int, string, Exception?> LogUnexpectedFailure = LoggerMessage.Define<string, string, int, string>(
        LogLevel.Error,
        new EventId(1300, "DesktopOperationFailure"),
        "Unexpected desktop operation failure. operation={Operation} exceptionType={ExceptionType} exceptionHResult={ExceptionHResult} message={SafeMessage}");

    private readonly ILogger logger;

    public DesktopOperationDiagnostics(ILogger logger) =>
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void ReportUnexpectedFailure(string operation, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException)
            return;

        var safeOperation = string.IsNullOrWhiteSpace(operation) ? "desktop.unknown" : operation;
        try
        {
            LogUnexpectedFailure(
                logger,
                safeOperation,
                exception.GetType().Name,
                exception.HResult,
                GetSafeMessage(exception),
                null);
        }
        catch
        {
            // Diagnostics are best-effort and must never change a business operation.
        }
    }

    private static string GetSafeMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Local resource access was denied.",
        IOException => "Local I/O operation failed.",
        TimeoutException => "Operation timed out.",
        InvalidOperationException => "Operation state was invalid.",
        ArgumentException => "An operation argument was rejected.",
        _ when exception.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) => "SQLite operation failed.",
        _ => "Unexpected operation failure."
    };
}
