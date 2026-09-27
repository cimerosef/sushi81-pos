namespace Sushi81.Pos.Application.Foundation;

/// <summary>The two fixed runtime identities supported by Sushi81 POS.</summary>
public sealed record DeploymentProfile
{
    public static DeploymentProfile Production { get; } = new("prod", "Sushi81 POS", false);

    public static DeploymentProfile PreProduction { get; } = new("preprod", "Sushi81 POS PREPROD", true);

    private DeploymentProfile(string value, string applicationIdentity, bool isPreProduction)
    {
        Value = value;
        ApplicationIdentity = applicationIdentity;
        IsPreProduction = isPreProduction;
    }

    public string Value { get; }

    public string ApplicationIdentity { get; }

    public bool IsPreProduction { get; }

    public string DataRootName => IsPreProduction ? "Sushi81 POS PREPROD" : "Sushi81 POS";

    public static bool TryParse(string? value, out DeploymentProfile profile)
    {
        profile = value switch
        {
            "prod" => Production,
            "preprod" => PreProduction,
            _ => null!
        };
        return profile is not null;
    }
}
