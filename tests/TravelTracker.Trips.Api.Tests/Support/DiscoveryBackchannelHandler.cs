using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Trips.Api.Tests.Support;

/** Serves test-only issuer metadata and public signing keys through the production JWT handler. */
internal sealed class DiscoveryBackchannelHandler(RSAParameters publicKey, string issuer) : HttpMessageHandler
{
    /**
     * Answers only the configured test authority's discovery and key URLs.
     *
     * @param request JWT metadata request.
     * @param cancellationToken Cancels metadata retrieval.
     * @return Discovery metadata or the public signing key.
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
                            kid = "trips-test-key",
                            n = Base64UrlEncoder.Encode(publicKey.Modulus!),
                            e = Base64UrlEncoder.Encode(publicKey.Exponent!)
                        }
                    }
                })
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
