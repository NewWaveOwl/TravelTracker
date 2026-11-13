namespace TravelTracker.Frontend.Auth;

/**
 * Holds the Supabase project URL and public application key; privileged service keys are never accepted.
 * @param Url The HTTPS project origin.
 * @param PublishableKey The publishable or legacy anonymous key.
 */
public sealed record SupabaseSettings(string Url, string PublishableKey)
{
    public string Issuer => Url + "/auth/v1";

    /**
     * Reads complete public Supabase settings and rejects URLs containing credentials or paths.
     * @param configuration Host-owned configuration.
     * @return Valid settings, or null while setup is incomplete.
     */
    public static SupabaseSettings? TryRead(IConfiguration configuration)
    {
        var url = configuration["Supabase:Url"]?.Trim().TrimEnd('/');
        var key = configuration["Supabase:PublishableKey"]?.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Port != 443 || string.IsNullOrWhiteSpace(key) ||
            key.StartsWith("sb_secret_", StringComparison.Ordinal)) return null;
        // Legacy public keys are JWTs. Their role must be anon, never service_role.
        if (!key.StartsWith("sb_publishable_", StringComparison.Ordinal))
        {
            try
            {
                var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(key);
                if (!jwt.TryGetClaim("role", out var role) || role.Value != "anon") return null;
            }
            catch (ArgumentException) { return null; }
        }
        return new(uri.GetLeftPart(UriPartial.Authority), key);
    }
}
