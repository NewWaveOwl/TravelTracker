using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using TravelTracker.Security.Authentication;

namespace TravelTracker.Security.Hosting;

/** Validates authenticated backend destinations without echoing configured URLs or tokens. */
public static class DependencyConfiguration
{
    /**
     * Requires the Users credential (static token or auto-refreshing Supabase service account) and a credential-free HTTPS endpoint in Production.
     * @param builder The dependent service host.
     */
    public static void ValidateUsersDependency(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction())
            return;
        RequireHttps(builder.Configuration["Users:InternalBaseUrl"], "Users:InternalBaseUrl");
        if (SupabaseServiceAccessTokenProvider.HasRefreshCredentials(builder.Configuration))
            return;
        var token = builder.Configuration["Users:ServiceAccessToken"];
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Users:ServiceAccessToken must contain one bearer token in Production.");
    }

    /**
     * Rejects a dependency URL if it could carry credentials or use insecure transport.
     * @param value The configured URL.
     * @param key The fixed setting name to report on failure.
     */
    public static void RequireHttps(string? value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException($"{key} must be an absolute credential-free HTTPS URL.");
    }
}
