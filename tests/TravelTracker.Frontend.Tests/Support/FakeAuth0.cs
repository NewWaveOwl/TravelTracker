using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Frontend.Tests.Support;

/**
 * Plays the Auth0 tenant in process: discovery, signing keys and the token endpoint, so the real
 * OpenID Connect handler runs end to end without a browser or a network.
 *
 * @param clientId The client id the id token is issued to.
 */
public sealed class FakeAuth0(string clientId) : HttpMessageHandler
{
    public const string Authority = "https://tenant.example.test/";

    readonly RSA signingKey = RSA.Create(2048);

    /** The nonce from the authorize redirect; the id token echoes it unless a test overrides it. */
    public string? Nonce { get; set; }

    /** The PKCE challenge from the authorize redirect, checked against the verifier at the token endpoint. */
    public string? CodeChallenge { get; set; }

    public string AccessToken { get; set; } = "access-token-1";

    public int ExpiresInSeconds { get; set; } = 3600;

    public string? IdTokenNonceOverride { get; set; }

    public string IdTokenIssuer { get; set; } = Authority;

    public string? IdTokenAudience { get; set; }

    public TimeSpan IdTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /** Signs the id token with a key that is not in the published key set. */
    public bool SignWithUnpublishedKey { get; set; }

    /** Leaves access_token out of the token response, as a misconfigured API grant can. */
    public bool OmitAccessToken { get; set; }

    /** Makes the token endpoint answer with this status and an OAuth error body instead of tokens. */
    public HttpStatusCode? TokenEndpointFailure { get; set; }

    public List<Dictionary<string, string>> TokenRequests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (url == $"{Authority}.well-known/openid-configuration")
            return Json(new
            {
                issuer = Authority,
                authorization_endpoint = $"{Authority}authorize",
                token_endpoint = $"{Authority}oauth/token",
                jwks_uri = $"{Authority}.well-known/jwks.json",
                end_session_endpoint = $"{Authority}oidc/logout",
                id_token_signing_alg_values_supported = new[] { "RS256" },
            });
        if (url == $"{Authority}.well-known/jwks.json")
        {
            var parameters = signingKey.ExportParameters(false);
            return Json(new { keys = new[] { new { kty = "RSA", use = "sig", kid = "fake", n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } });
        }
        if (url == $"{Authority}oauth/token" && request.Method == HttpMethod.Post)
            return IssueTokensAsync(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    async Task<HttpResponseMessage> IssueTokensAsync(HttpRequestMessage request)
    {
        var form = (await request.Content!.ReadAsStringAsync()).Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));
        TokenRequests.Add(form);
        var verifierHash = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form.GetValueOrDefault("code_verifier", ""))));
        if (verifierHash != CodeChallenge)
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant", error_description = "PKCE verifier mismatch" }) };
        if (TokenEndpointFailure is { } failure)
            return new HttpResponseMessage(failure) { Content = JsonContent.Create(new { error = "server_error", error_description = "tenant-internal-detail" }) };
        var now = DateTime.UtcNow;
        using var unpublished = RSA.Create(2048);
        var idToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            IdTokenIssuer, IdTokenAudience ?? clientId,
            [new Claim("sub", "auth0|traveller-1"), new Claim("nonce", IdTokenNonceOverride ?? Nonce ?? ""), new Claim("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
            now.AddMinutes(-1), now.Add(IdTokenLifetime),
            new SigningCredentials(new RsaSecurityKey(SignWithUnpublishedKey ? unpublished : signingKey) { KeyId = "fake" }, SecurityAlgorithms.RsaSha256)));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(OmitAccessToken ? new { id_token = idToken, token_type = "Bearer", expires_in = ExpiresInSeconds } : (object)new { access_token = AccessToken, id_token = idToken, token_type = "Bearer", expires_in = ExpiresInSeconds }),
        };
    }

    static Task<HttpResponseMessage> Json(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
}
