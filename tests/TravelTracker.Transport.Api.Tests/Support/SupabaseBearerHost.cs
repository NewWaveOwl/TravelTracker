using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TravelTracker.Api.Authentication;

namespace TravelTracker.Transport.Api.Tests;

/** Runs the production bearer handler against an isolated Supabase-shaped JWKS endpoint. */
internal sealed class SupabaseBearerHost : IDisposable
{
    internal const string Issuer = "https://project.example.test/auth/v1";
    internal const string Subject = "10d5c7fe-8989-4a43-b68a-9c45e9a73bd8";
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly WebApplication server;
    readonly HttpClient backchannel;
    internal List<string> RequestedUrls { get; } = [];
    internal HttpClient Client { get; }

    /** Starts an in-process resource server with direct JWKS retrieval and no Auth0 dependency. */
    internal SupabaseBearerHost()
    {
        var publicKey = key.ExportParameters(false);
        backchannel = new HttpClient(new KeyHandler(this, publicKey));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var services = builder.Services;
        services.AddRouting();
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Backchannel = backchannel);
        services.AddApiBearerAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Provider"] = "Supabase",
            ["Authentication:Bearer:Authority"] = Issuer,
            ["Authentication:Bearer:Issuer"] = Issuer,
            ["Authentication:Bearer:Audience"] = "authenticated",
            ["Authentication:Bearer:ClockSkewSeconds"] = "0",
            ["Authentication:Bearer:ValidAlgorithms:0"] = "ES256"
        }).Build());
        server = builder.Build();
        server.UseAuthentication();
        server.UseAuthorization();
        server.MapGet("/private", () => "ok").RequireAuthorization(ScopePolicies.CatalogueRead);
        server.StartAsync().GetAwaiter().GetResult();
        Client = server.GetTestClient();
    }

    /**
     * Issues an ES256 test token with one intentionally controlled failure.
     * @param scenario The validation or authorization case under test.
     * @return A signed token; no test bypasses the production bearer handler.
     */
    internal string Token(string scenario)
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var claims = new List<Claim> { new("sub", scenario == "subject" ? "auth0|old-user" : Subject) };
        if (scenario != "role-missing") claims.Add(new("role", scenario == "service-role" ? "service_role" : "authenticated"));
        claims.Add(new("is_anonymous", scenario == "anonymous" ? "true" : "false", ClaimValueTypes.Boolean));
        if (scenario == "metadata-scope")
            claims.Add(new("user_metadata", "{\"scope\":\"catalogue.read\"}", JsonClaimValueTypes.Json));
        else if (scenario != "scope") claims.Add(new("scope", "catalogue.read"));
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            scenario == "issuer" ? "https://other.example.test/auth/v1" : Issuer,
            scenario == "audience" ? "service_role" : "authenticated", claims,
            now.AddMinutes(-10), scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5),
            new SigningCredentials(new ECDsaSecurityKey(scenario == "signature" ? other : key) { KeyId = "supabase-test-key" }, "ES256")));
    }

    /** Releases only the isolated HTTP host and test signing material. */
    public void Dispose()
    {
        Client.Dispose();
        server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        backchannel.Dispose();
        key.Dispose();
    }

    /** Serves only the trusted issuer's JWKS; a discovery request fails this fixture. */
    sealed class KeyHandler(SupabaseBearerHost host, ECParameters key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            host.RequestedUrls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(request.RequestUri.AbsoluteUri == Issuer + "/.well-known/jwks.json"
                ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new
                {
                    keys = new[] { new { kty = "EC", crv = "P-256", use = "sig", alg = "ES256", kid = "supabase-test-key",
                        x = Base64UrlEncoder.Encode(key.Q.X!), y = Base64UrlEncoder.Encode(key.Q.Y!) } }
                })
            });
        }
    }
}
