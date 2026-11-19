namespace TravelTracker.Security.Authentication;

/**
 * Defines deployment-owned resource-server settings; the public key is not a secret, but no private key belongs here.
 */
public sealed class BearerSettings
{
    /** Configuration section that binds these settings. */
    public const string SectionName = "Authentication:Bearer";
    /** PEM public key (SubjectPublicKeyInfo) that verifies token signatures. The Users API derives it from its own private key when blank. */
    public string PublicKey { get; set; } = "";
    /** Exact issuer value the tokens must carry. */
    public string Issuer { get; set; } = "";
    /** Audience this API accepts. */
    public string Audience { get; set; } = "";
    /** Name of the claim that carries the granted scopes. */
    public string ScopeClaimType { get; set; } = "scope";
    /** Allowed clock difference in seconds when checking token lifetime, from 0 to 120. */
    public int ClockSkewSeconds { get; set; } = 30;
    /** Accepted asymmetric JWT signing algorithms. */
    public string[] ValidAlgorithms { get; set; } = ["ES256"];
}
