namespace TravelTracker.Security.Authentication;

/**
 * Defines deployment-owned resource-server settings; the public key is not a secret, but no private key belongs here.
 */
public sealed class BearerSettings
{
    /** Configuration section that binds these settings. */
    public const string SectionName = "Authentication:Bearer";
    /** Provider value for tokens issued by the Users API and verified with its public key. */
    public const string LocalProvider = "Local";
    /** Provider value for tokens issued by Supabase Auth and verified with its published signing keys. */
    public const string SupabaseProvider = "Supabase";
    /** Which identity provider issues the tokens, Local or Supabase. */
    public string Provider { get; set; } = LocalProvider;
    /** Absolute HTTPS address of the Supabase Auth service (Supabase only; Local ignores it). */
    public string Authority { get; set; } = "";
    /** PEM public key (SubjectPublicKeyInfo) that verifies token signatures (Local only). The Users API derives it from its own private key when blank. */
    public string PublicKey { get; set; } = "";
    /** Exact issuer value the tokens must carry. For Supabase this is the project's HTTPS /auth/v1 URL. */
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
