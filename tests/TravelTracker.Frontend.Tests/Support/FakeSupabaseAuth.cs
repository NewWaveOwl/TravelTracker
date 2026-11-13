using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Frontend.Tests.Support;

/** Provides signed Supabase token, user and logout responses without live network access. */
public sealed class FakeSupabaseAuth : HttpMessageHandler
{
    public const string Origin = "https://project.supabase.test";
    public const string Subject = "c614a53c-1ff7-43b3-bab0-37a68c806a72";
    readonly RSA signingKey = RSA.Create(2048);
    readonly ECDsa ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public string Issuer { get; set; } = Origin + "/auth/v1";
    public string Audience { get; set; } = "authenticated";
    public bool UseEcKey { get; set; }
    public bool InvalidSignature { get; set; }
    public bool Verified { get; set; } = true;
    public bool Expired { get; set; }
    public bool RejectPassword { get; set; }
    public bool LogoutUnavailable { get; set; }
    public bool Anonymous { get; set; }
    public bool ConfirmationRejected { get; set; }
    public string Scope { get; set; } = "users.read users.write catalogue.read trips.read trips.write";
    public int PasswordRequests { get; private set; }
    public int SignupRequests { get; private set; }
    public int LogoutRequests { get; private set; }
    public int ConfirmationRequests { get; private set; }
    public string? LastAccessToken { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("project.supabase.test", request.RequestUri!.Host);
        Assert.Equal("sb_publishable_fixture", Assert.Single(request.Headers.GetValues("apikey")));
        switch (request.RequestUri.AbsolutePath)
        {
            case "/auth/v1/token":
                PasswordRequests++;
                Assert.Equal("?grant_type=password", request.RequestUri.Query);
                var submitted = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
                Assert.NotNull(submitted);
                if (RejectPassword) return Json(new { error = "invalid_grant", error_description = "private-provider-diagnostic" }, HttpStatusCode.BadRequest);
                return Json(new { access_token = CreateToken(), expires_in = 3600 });
            case "/auth/v1/.well-known/jwks.json":
                var rsa = signingKey.ExportParameters(false);
                var ec = ecKey.ExportParameters(false);
                return UseEcKey
                    ? Json(new { keys = new[] { new { kty = "EC", use = "sig", crv = "P-256", kid = "ec", x = Base64UrlEncoder.Encode(ec.Q.X!), y = Base64UrlEncoder.Encode(ec.Q.Y!) } } })
                    : Json(new { keys = new[] { new { kty = "RSA", use = "sig", kid = "rsa", n = Base64UrlEncoder.Encode(rsa.Modulus!), e = Base64UrlEncoder.Encode(rsa.Exponent!) } } });
            case "/auth/v1/user":
                Assert.Equal(LastAccessToken, request.Headers.Authorization?.Parameter);
                return Json(new { id = Subject, email = "traveller@example.test", email_confirmed_at = Verified ? "2026-01-01T00:00:00Z" : null, is_anonymous = false });
            case "/auth/v1/signup":
                SignupRequests++;
                return Json(new { user = new { id = Subject }, session = (object?)null });
            case "/auth/v1/verify":
                ConfirmationRequests++;
                var verification = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
                Assert.Equal("email", verification!["type"]);
                Assert.Equal("confirmation-hash-fixture", verification["token_hash"]);
                return Json(new { access_token = "must-never-be-used", refresh_token = "must-never-be-stored" },
                    ConfirmationRejected ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
            case "/auth/v1/logout":
                LogoutRequests++;
                Assert.Equal("?scope=local", request.RequestUri.Query);
                return new HttpResponseMessage(LogoutUnavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
            default: return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /**
     * Issues a token whose independently controlled claims exercise real verification.
     * @return The signed test access token.
     */
    string CreateToken()
    {
        using var wrong = RSA.Create(2048);
        SecurityKey key = UseEcKey ? new ECDsaSecurityKey(ecKey) { KeyId = "ec" }
            : new RsaSecurityKey(InvalidSignature ? wrong : signingKey) { KeyId = "rsa" };
        var now = DateTime.UtcNow;
        return LastAccessToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            Issuer, Audience,
            [new Claim("sub", Subject), new Claim("email", "traveller@example.test"), new Claim("role", "authenticated"),
             new Claim("scope", Scope), new Claim("is_anonymous", Anonymous ? "true" : "false", ClaimValueTypes.Boolean),
             new Claim("user_metadata", "{\"scope\":\"users.access\",\"role\":\"administrator\"}", JsonClaimValueTypes.Json)],
            now.AddHours(-1), Expired ? now.AddSeconds(-1) : now.AddHours(1),
            new SigningCredentials(key, UseEcKey ? SecurityAlgorithms.EcdsaSha256 : SecurityAlgorithms.RsaSha256)));
    }

    static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(body) };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { signingKey.Dispose(); ecKey.Dispose(); }
        base.Dispose(disposing);
    }
}
