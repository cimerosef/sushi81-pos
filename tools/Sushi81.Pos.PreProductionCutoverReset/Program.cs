using System.Text.Json;

namespace Sushi81.Pos.PreProductionCutoverReset;

internal static class Program
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
            {
                PrintUsage();
                return 0;
            }

            var request = ParseRequest(args);
            var dataRoot = GetDefaultDataRoot();
            Console.WriteLine($"Resolved data root: {dataRoot}");
            var service = new PreProductionCutoverService(dataRoot, new WindowsCutoverHost(), report => Console.WriteLine(JsonSerializer.Serialize(report, ReportJsonOptions)));
            var result = service.Run(request);

            Console.WriteLine(result.Message);
            if (result.BackupDirectory is not null)
            {
                Console.WriteLine($"Local rollback backup: {result.BackupDirectory}");
                Console.WriteLine($"live.db backup SHA-256: {result.LiveDatabaseBackupSha256}");
                Console.WriteLine($"business_data_revision: {result.BusinessDataRevisionBefore} -> {result.BusinessDataRevisionAfter}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CUTOVER REFUSED: {exception.Message}");
            return 1;
        }
    }

    private static CutoverRequest ParseRequest(string[] args)
    {
        if (args.Length == 0) return new(false, null, null);
        if (args.Length != 5
            || !string.Equals(args[0], "--execute", StringComparison.Ordinal)
            || !string.Equals(args[1], "--confirmation", StringComparison.Ordinal)
            || !string.Equals(args[3], "--confirm-root", StringComparison.Ordinal))
        {
            throw new ArgumentException("Expected no arguments for dry-run, or --execute --confirmation DELETE-ALL-TEST-BUSINESS-DATA --confirm-root <exact resolved root>.");
        }

        return new(true, args[2], args[4]);
    }

    private static string GetDefaultDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) throw new InvalidOperationException("Windows LocalApplicationData could not be resolved.");
        return Path.GetFullPath(Path.Combine(localAppData, "Sushi81 POS"));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Sushi81 POS pre-production cutover reset");
        Console.WriteLine("No arguments: validated read-only dry run.");
        Console.WriteLine("Mutation: --execute --confirmation DELETE-ALL-TEST-BUSINESS-DATA --confirm-root <exact resolved data root>");
        Console.WriteLine("The project controller must close GitHub Issue #4 before owner execution.");
    }
}
