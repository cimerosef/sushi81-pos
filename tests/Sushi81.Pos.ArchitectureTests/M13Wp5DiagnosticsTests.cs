using System.IO;
using Microsoft.Extensions.Logging;
using Sushi81.Pos.Desktop;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M13Wp5DiagnosticsTests
{
    [TestMethod]
    public void DiagnosticsReportsUnexpectedFailuresSkipsCancellationAndToleratesLoggerFailure()
    {
        var logger = new RecordingLogger();
        var diagnostics = new DesktopOperationDiagnostics(logger);

        const string customerText = "synthetic customer address and order comment";
        var secretBearingException = new InvalidOperationException(
            "synthetic failure 0612345678 customer@example.test Bearer " + ('a' * 40)
            + string.Concat(" https://", "user", ":", "password", "@example.test/api?access_token=query-secret ")
            + string.Concat("ghp_", new string('b', 40)) + " secret-field=synthetic-secret " + customerText);
        diagnostics.ReportUnexpectedFailure("order-entry.synthetic", secretBearingException);
        diagnostics.ReportUnexpectedFailure("order-entry.cancelled", new OperationCanceledException());

        Assert.AreEqual(1, logger.CallCount);
        Assert.AreEqual(LogLevel.Error, logger.Level);
        Assert.AreEqual(1300, logger.EventId.Id);
        Assert.AreEqual("DesktopOperationFailure", logger.EventId.Name);
        StringAssert.Contains(logger.Message!, "order-entry.synthetic");
        StringAssert.Contains(logger.Message!, "exceptionType=InvalidOperationException");
        StringAssert.Contains(logger.Message!, "message=Operation state was invalid.");
        Assert.IsNull(logger.Exception, "Free-form exception details can contain order or customer data and must not be serialized.");
        Assert.IsFalse(logger.Message!.Contains("0612345678", StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains("customer@example.test", StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains("Bearer ", StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains("user:password@", StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains("query-secret", StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains("synthetic-secret", StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains(new string('b', 40), StringComparison.Ordinal));
        Assert.IsFalse(logger.Message.Contains(customerText, StringComparison.Ordinal));

        var failing = new DesktopOperationDiagnostics(new ThrowingLogger());
        failing.ReportUnexpectedFailure("order-entry.log-failure", new IOException("synthetic logger failure"));
    }

    [TestMethod]
    public void GenericOperationFailedExceptionPathsReachTheDiagnosticSeam()
    {
        var root = FindRepositoryRoot();
        AssertOperationFailedCatchPathsReport(root, "src/Sushi81.Pos.Desktop/MainWindow.xaml.cs", minimumPaths: 7);
        AssertOperationFailedCatchPathsReport(root, "src/Sushi81.Pos.Desktop/OrderEntryShellViewModel.cs", minimumPaths: 6);
        AssertOperationFailedCatchPathsReport(root, "src/Sushi81.Pos.Desktop/OrderLifecycleShellViewModel.cs", minimumPaths: 1);

        var catalogue = File.ReadAllText(Path.Combine(root, "src/Sushi81.Pos.Desktop/M03ShellViewModel.cs"));
        StringAssert.Contains(catalogue, "diagnostics?.ReportUnexpectedFailure(\"catalogue.filter-refresh\", exception)");
    }

    [TestMethod]
    public void WpfSourceDoesNotIntroduceSynchronousBlockingWaits()
    {
        var root = FindRepositoryRoot();
        var desktop = Path.Combine(root, "src/Sushi81.Pos.Desktop");
        var blockingCall = new System.Text.RegularExpressions.Regex(
            @"(?<![A-Za-z0-9_])(?:Thread\s*\.\s*Sleep\s*\(|\.\s*Wait\s*\(|\.\s*GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\()",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var resultAccess = new System.Text.RegularExpressions.Regex(
            @"(?<receiver>[A-Za-z_]\w*)\s*\.\s*Result\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        foreach (var sourcePath in Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourcePath);
            var relativePath = Path.GetRelativePath(root, sourcePath);
            Assert.IsFalse(blockingCall.IsMatch(source), $"Synchronous blocking wait found in {relativePath}.");
            foreach (System.Text.RegularExpressions.Match match in resultAccess.Matches(source))
            {
                Assert.AreEqual("outcome", match.Groups["receiver"].Value,
                    $"Unreviewed synchronous Result access found in {relativePath}.");
            }
        }
    }

    private static void AssertOperationFailedCatchPathsReport(string root, string relativePath, int minimumPaths)
    {
        var lines = File.ReadAllText(Path.Combine(root, relativePath)).Split('\n');
        var checkedPaths = 0;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!lines[lineIndex].Contains("OperationFailed", StringComparison.Ordinal)) continue;
            var catchIndex = -1;
            for (var previous = lineIndex; previous >= Math.Max(0, lineIndex - 8); previous--)
            {
                if (lines[previous].Contains("catch", StringComparison.Ordinal)
                    && lines[previous].Contains("Exception", StringComparison.Ordinal))
                {
                    catchIndex = previous;
                    break;
                }
            }
            if (catchIndex < 0) continue;

            var catchText = string.Join('\n', lines.Skip(catchIndex).Take(lineIndex - catchIndex + 1));
            Assert.IsTrue(
                catchText.Contains("ReportUnexpectedFailure", StringComparison.Ordinal),
                $"{relativePath}:{lineIndex + 1} displays generic OperationFailed without reporting the caught exception.");
            checkedPaths++;
        }

        Assert.IsGreaterThanOrEqualTo(minimumPaths, checkedPaths, $"Expected at least {minimumPaths} generic OperationFailed catch path(s) in {relativePath}.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Sushi81 POS repository root from the test output directory.");
    }

    private sealed class RecordingLogger : ILogger
    {
        public int CallCount { get; private set; }
        public LogLevel Level { get; private set; }
        public EventId EventId { get; private set; }
        public string? Message { get; private set; }
        public Exception? Exception { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            CallCount++;
            Level = logLevel;
            EventId = eventId;
            Message = formatter(state, exception);
            Exception = exception;
        }
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new IOException("synthetic logger failure");
    }
}
