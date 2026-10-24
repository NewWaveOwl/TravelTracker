namespace TravelTracker.Api.Authentication;

/**
 * Defines deployment-owned resource-server settings; no signing secrets belong here.
 */
public sealed class BearerSettings
{
    public const string SectionName = "Authentication:Bearer";
    public string Authority { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string ScopeClaimType { get; set; } = "scope";
}
