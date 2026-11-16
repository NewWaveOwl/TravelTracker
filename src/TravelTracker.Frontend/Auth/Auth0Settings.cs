using System.Text.RegularExpressions;

namespace TravelTracker.Frontend.Auth;

/**
 * Auth0 application settings for hosted sign-in; the client secret is server-side only and must never reach the browser.
 *
 * @param Domain The tenant hostname only, such as your-tenant-region.auth0.com.
 * @param ClientId The Auth0 application client id.
 * @param ClientSecret The optional client secret; leave empty for a public client using PKCE only.
 * @param Audience The API identifier that access tokens are issued for.
 * @param Scope The space separated scopes requested at sign-in; DefaultScope applies when the configuration leaves it empty.
 */
public sealed partial record Auth0Settings(string Domain, string ClientId, string? ClientSecret, string Audience, string Scope)
{
    /** the scopes requested when Auth0:Scope is empty. */
    public const string DefaultScope = "openid profile email catalogue.read catalogue.write users.read users.write users.admin settings.read trips.read trips.write";

    /** the tenant authority URL derived from the domain. */
    public string Authority => $"https://{Domain}/";

    /**
     * Reads the Auth0 section without accepting malformed hosts.
     *
     * @param configuration The application configuration.
     * @param logger Receives the reason when a partly filled section is rejected; never receives secrets.
     * @return Complete settings, or null until sign-in is configured.
     */
    public static Auth0Settings? TryRead(IConfiguration configuration, ILogger? logger = null)
    {
        var section = configuration.GetSection("Auth0");
        var domain = section["Domain"]?.Trim();
        var clientId = section["ClientId"]?.Trim();
        var audience = section["Audience"]?.Trim();
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(audience))
            return null;
        if (!ValidDomain().IsMatch(domain) || !domain.Contains('.') || domain.Contains(".."))
        {
            logger?.LogWarning("Auth0:Domain was rejected; it must be the tenant hostname only (no scheme or path), such as your-tenant.auth0.com");
            return null;
        }
        var scope = section["Scope"]?.Trim();
        var secret = section["ClientSecret"]?.Trim();
        return new Auth0Settings(domain, clientId, string.IsNullOrEmpty(secret) ? null : secret, audience, string.IsNullOrEmpty(scope) ? DefaultScope : scope);
    }

    [GeneratedRegex("^[a-z0-9.-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ValidDomain();
}
