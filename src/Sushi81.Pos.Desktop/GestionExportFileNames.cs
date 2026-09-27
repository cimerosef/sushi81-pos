using Sushi81.Pos.Application.Foundation;

namespace Sushi81.Pos.Desktop;

public static class GestionExportFileNames
{
    public static string NewExport(DateTime localTime, DeploymentProfile profile) =>
        ApplyPrefix($"Sushi81_POS_Export_{localTime:yyyyMMdd_HHmmss}.xlsx", profile);

    public static string Regenerate(DateTimeOffset generatedAt, Guid batchId, DeploymentProfile profile) =>
        ApplyPrefix($"Sushi81_POS_Export_{generatedAt.ToLocalTime():yyyyMMdd_HHmmss}_{batchId:D}.xlsx", profile);

    public static string Retry(DateTimeOffset preparedAt, Guid batchId, DeploymentProfile profile) =>
        ApplyPrefix($"Sushi81_POS_Export_Retry_{preparedAt.ToLocalTime():yyyyMMdd_HHmmss}_{batchId:D}.xlsx", profile);

    private static string ApplyPrefix(string fileName, DeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.IsPreProduction ? $"PREPROD_{fileName}" : fileName;
    }
}
