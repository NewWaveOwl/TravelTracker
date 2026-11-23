namespace TravelTracker.Security.Authentication;

/**
 * Defines deployment-owned resource-server settings; no signing secrets belong here.
 */
public sealed class BearerSettings
{
    /** Configuration section that binds these settings. */
    public const string SectionName = "Authentication:Bearer";
    /** Identity provider kind, Oidc or Supabase, which selects how tokens are validated. */
    public string Provider { get; set; } = "Oidc";
    /** Absolute HTTPS address of the identity provider that publishes discovery metadata. */
    public string Authority { get; set; } = "";
    /** Exact issuer value the tokens and discovery metadata must carry. */
    public string Issuer { get; set; } = "";
    /** Audience this API accepts. */
    public string Audience { get; set; } = "";
    /** Name of the claim that carries the granted scopes. */
    public string ScopeClaimType { get; set; } = "scope";
    /** Allowed clock difference in seconds when checking token lifetime, from 0 to 120. */
    public int ClockSkewSeconds { get; set; } = 30;
    /** Accepted asymmetric JWT signing algorithms. */
    public string[] ValidAlgorithms { get; set; } = ["RS256"];
}
