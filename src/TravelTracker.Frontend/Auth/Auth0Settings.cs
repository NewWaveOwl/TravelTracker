using System.Text.RegularExpressions;

namespace TravelTracker.Frontend.Auth;

/**
 * Public Auth0 application settings for hosted sign-in.
 *
 * @param Domain The tenant hostname only, such as your-tenant-region.auth0.com.
 * @param ClientId The Auth0 application client id.
 * @param ClientSecret The optional client secret; leave empty for a public client using PKCE only.
 * @param Audience The API identifier that access tokens are issued for.
 * @param Scope The space separated scopes requested at sign-in.
 */
public sealed partial record Auth0Settings(string Domain, string ClientId, string? ClientSecret, string Audience, string Scope)
{
    public const string DefaultScope = "openid profile email catalogue.read users.read settings.read";

    public string Authority => $"https://{Domain}/";

    /**
     * Reads the Auth0 section without accepting malformed hosts.
     *
     * @param configuration The application configuration.
     * @return Complete settings, or null until sign-in is configured.
     */
    public static Auth0Settings? TryRead(IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth0");
        var domain = section["Domain"]?.Trim();
        var clientId = section["ClientId"]?.Trim();
        var audience = section["Audience"]?.Trim();
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(audience) || !ValidDomain().IsMatch(domain) || !domain.Contains('.') || domain.Contains(".."))
            return null;
        var scope = section["Scope"]?.Trim();
        var secret = section["ClientSecret"]?.Trim();
        return new Auth0Settings(domain, clientId, string.IsNullOrEmpty(secret) ? null : secret, audience, string.IsNullOrEmpty(scope) ? DefaultScope : scope);
    }

    [GeneratedRegex("^[a-z0-9.-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ValidDomain();
}
