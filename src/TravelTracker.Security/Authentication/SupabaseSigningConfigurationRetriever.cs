using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Api.Authentication;

/**
 * Loads rotating public signing keys from the configured Supabase issuer, without OIDC discovery.
 * @param issuer The deployment-owned, exact HTTPS issuer accepted by the resource server.
 */
internal sealed class SupabaseSigningConfigurationRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    /**
     * Retrieves only the trusted issuer's JWKS and binds its keys to the pinned issuer.
     * @param address The expected JWKS endpoint supplied by the configuration manager.
     * @param retriever HTTPS-only document transport.
     * @param cancel Cancels the key retrieval.
     * @return Signing metadata refreshed by the framework configuration manager.
     */
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        if (!string.Equals(address, issuer + "/.well-known/jwks.json", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected Supabase signing-key endpoint.");
        var jwks = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
        var metadata = new OpenIdConnectConfiguration { Issuer = issuer, JwksUri = address };
        foreach (var key in jwks.GetSigningKeys())
            metadata.SigningKeys.Add(key);
        if (metadata.SigningKeys.Count == 0)
            throw new InvalidOperationException("Supabase did not publish a usable asymmetric signing key.");
        return metadata;
    }
}
