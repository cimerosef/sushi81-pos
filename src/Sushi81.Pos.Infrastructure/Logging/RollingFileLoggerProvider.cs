using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Logging;

/// <summary>Small bounded UTF-8 rolling-file provider for technical diagnostics only.</summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const long MaximumFileBytes = 10 * 1024 * 1024;
    private const int RetainedFiles = 14;
    private readonly IAppPaths paths;
    private readonly TimeProvider timeProvider;
    private readonly object writeLock = new();
    private bool disposed;

    public RollingFileLoggerProvider(IAppPaths paths, TimeProvider timeProvider)
    {
        this.paths = paths;
        this.timeProvider = timeProvider;
        paths.EnsureInitialized();
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new RollingFileLogger(this, categoryName);
    }

    public void Dispose() => disposed = true;

    private void Write(LogLevel logLevel, EventId eventId, string category, string message, Exception? exception)
    {
        if (disposed)
        {
            return;
        }

        var timestamp = timeProvider.GetUtcNow();
        var safeMessage = SensitiveDataRedactor.Redact(message);
        var exceptionPart = exception is null
            ? string.Empty
            : $" exception={exception.GetType().Name}:{SensitiveDataRedactor.Redact(exception.Message)}";
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp:O} level={logLevel} category={category} eventId={eventId.Id} eventName={eventId.Name ?? "none"} message={safeMessage}{exceptionPart}{Environment.NewLine}");

        lock (writeLock)
        {
            var targetPath = GetCurrentLogPath(timestamp);
            File.AppendAllText(targetPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            ApplyRetention();
        }
    }

    private string GetCurrentLogPath(DateTimeOffset timestamp)
    {
        var date = timestamp.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (var sequence = 0; ; sequence++)
        {
            var candidate = Path.Combine(paths.LogsDirectory, $"sushi81-{date}-{sequence:D3}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaximumFileBytes)
            {
                return candidate;
            }
        }
    }

    private void ApplyRetention()
    {
        foreach (var obsolete in Directory.EnumerateFiles(paths.LogsDirectory, "sushi81-*.log")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Skip(RetainedFiles))
        {
            File.Delete(obsolete);
        }
    }

    private sealed class RollingFileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
                return;

            try
            {
                provider.Write(logLevel, eventId, category, formatter(state, exception), exception);
            }
            catch
            {
                // Logging is diagnostic only. A full disk or inaccessible log path must not
                // replace or alter the result of the operation being diagnosed.
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
