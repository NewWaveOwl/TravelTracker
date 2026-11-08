using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * serves test-only issuer metadata and the public signing key through the production JWT handler.
 * @param publicKey the test issuer's public RSA key only.
 * @param issuer the exact issuer published in discovery.
 */
internal sealed class DiscoveryBackchannelHandler(RSAParameters publicKey, string issuer) : HttpMessageHandler
{
    /**
     * answers only the configured test authority's discovery and key addresses.
     * @param request the JWT metadata request.
     * @param cancellationToken cancels metadata retrieval.
     * @return discovery metadata, the public signing key, or 404 for any other address.
     */
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = request.RequestUri!.AbsoluteUri;
        if (url == $"{issuer}/.well-known/openid-configuration")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    issuer,
                    jwks_uri = $"{issuer}/keys",
                    id_token_signing_alg_values_supported = new[] { "RS256" }
                })
            });
        if (url == $"{issuer}/keys")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            kid = "settings-test-key",
                            n = Base64UrlEncoder.Encode(publicKey.Modulus!),
                            e = Base64UrlEncoder.Encode(publicKey.Exponent!)
                        }
                    }
                })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
