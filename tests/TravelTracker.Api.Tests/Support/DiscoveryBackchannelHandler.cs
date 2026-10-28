using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Api.Tests;

/**
 * Serves isolated discovery/JWKS responses through the framework's real metadata retriever.
 * @param publicKey The test issuer's public RSA key only.
 * @param issuer The exact issuer published in discovery.
 */
internal sealed class DiscoveryBackchannelHandler(RSAParameters publicKey, string issuer) : HttpMessageHandler
{
    internal List<string> RequestedUrls { get; } = [];

    /**
     * Answers only the configured authority's discovery and signing-key addresses.
     * @param request The framework's metadata request.
     * @param cancellationToken Cancels metadata retrieval.
     * @return A JSON discovery/JWKS response, or 404 for an unexpected URL.
     */
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = request.RequestUri!.AbsoluteUri;
        RequestedUrls.Add(url);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        if (url == $"{ApiFactory.Issuer}/.well-known/openid-configuration")
            response.Content = JsonContent.Create(new
            {
                issuer,
                jwks_uri = $"{ApiFactory.Issuer}/keys",
                id_token_signing_alg_values_supported = new[] { "RS256" }
            });
        else if (url == $"{ApiFactory.Issuer}/keys")
            response.Content = JsonContent.Create(new
            {
                keys = new[] { new
                {
                    kty = "RSA", use = "sig", kid = "test-signing-key",
                    n = Base64UrlEncoder.Encode(publicKey.Modulus!),
                    e = Base64UrlEncoder.Encode(publicKey.Exponent!)
                } }
            });
        else response.StatusCode = HttpStatusCode.NotFound;
        return Task.FromResult(response);
    }
}
