using Sushi81.Pos.Application.Foundation;

namespace Sushi81.Pos.Application.Foundation.Configuration;

/// <summary>Only local technical foundation configuration belongs here in M01.</summary>
public sealed record LocalConfiguration(
    string UiCulture = "fr-FR",
    string? OneDriveRoot = null,
    string? GitHubOwner = null,
    string? GitHubRepository = null,
    string GitHubReleaseTag = "sushi81-handoff-v1",
    string GitHubReleaseName = "Sushi81 POS Handoff Transport",
    string? GitHubCredentialTarget = null,
    string? DeviceDisplayName = null,
    string? KitchenPrinterQueueId = null,
    string? KitchenPrinterQueueName = null,
    string? CustomerPrinterQueueId = null,
    string? CustomerPrinterQueueName = null)
{
    public const string ProductionGitHubReleaseTag = "sushi81-handoff-v1";
    public const string ProductionGitHubReleaseName = "Sushi81 POS Handoff Transport";
    public const string PreProductionGitHubReleaseTag = "sushi81-handoff-preprod-v1";
    public const string PreProductionGitHubReleaseName = "Sushi81 POS PREPROD Handoff Transport";

    public static LocalConfiguration CreateDefaults(DeploymentProfile profile) =>
        profile.IsPreProduction
            ? new LocalConfiguration(
                GitHubReleaseTag: PreProductionGitHubReleaseTag,
                GitHubReleaseName: PreProductionGitHubReleaseName)
            : new LocalConfiguration();
}
