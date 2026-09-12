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
    string? CustomerPrinterQueueName = null);
