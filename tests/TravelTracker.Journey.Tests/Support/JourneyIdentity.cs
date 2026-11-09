using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Journey.Tests.Support;

/**
 * plays the identity provider for the whole journey: one signing key, issuer discovery and signed tokens.
 * every host validates against this same issuer through its production bearer handler.
 */
internal sealed class JourneyIdentity : HttpMessageHandler, IDisposable
{
    internal const string Issuer = "https://issuer.journey.test";
    internal const string Audience = "traveltracker-journey";
    private const string KeyId = "journey-key";

    private readonly RSA signingKey = RSA.Create(2048);

    /**
     * signs a token for a subject with the given scopes.
     * @param subject the validated user subject; "banned" and "deactivated" name those account standings.
     * @param scopes space-separated permissions, or null for no scope claim.
     * @param failure an optional invalid-token case: "audience", "expired" or "signature".
     * @return a bearer token every journey host accepts unless a failure case is requested.
     */
    internal string CreateToken(string subject, string? scopes, string? failure = null)
    {
        using var otherKey = RSA.Create(2048);
        var claims = new List<Claim> { new("sub", subject) };
        if (scopes is not null) claims.Add(new Claim("scope", scopes));
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new RsaSecurityKey(failure == "signature" ? otherKey : signingKey) { KeyId = KeyId },
            SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            Issuer,
            failure == "audience" ? "other-api" : Audience,
            claims,
            now.AddMinutes(failure == "expired" ? -10 : -1),
            now.AddMinutes(failure == "expired" ? -5 : 10),
            credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /**
     * answers the issuer's discovery document and key set.
     * @param request the metadata request from a host's bearer handler.
     * @param cancellationToken cancels the metadata request.
     * @return discovery metadata, the public key, or 404 for any other address.
     */
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url == $"{Issuer}/.well-known/openid-configuration")
            return Task.FromResult(Json(new { issuer = Issuer, jwks_uri = $"{Issuer}/keys", id_token_signing_alg_values_supported = new[] { "RS256" } }));
        if (url != $"{Issuer}/keys") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var publicKey = signingKey.ExportParameters(false);
        return Task.FromResult(Json(new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", kid = KeyId, n = Base64UrlEncoder.Encode(publicKey.Modulus!), e = Base64UrlEncoder.Encode(publicKey.Exponent!) }
            }
        }));
    }

    /** releases the signing key. */
    public new void Dispose()
    {
        signingKey.Dispose();
        base.Dispose();
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
