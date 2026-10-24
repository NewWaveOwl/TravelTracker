using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;

namespace TravelTracker.Api.Tests;

/**
 * Creates an isolated in-memory HTTP host for each test.
 */
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    internal const string Issuer = "https://issuer.example.test";
    internal const string Audience = "traveltracker-tests";
    private readonly RSA signingKey = RSA.Create(2048);
    internal string ScopeClaimType { get; init; } = "scope";

    /**
     * Issues a test-only RSA-signed token; production still uses provider discovery.
     * @param scopes The permissions to issue, or null for no scope claim.
     * @param failure An optional invalid-token scenario.
     * @param claimType An optional scope claim name override.
     * @return A serialized bearer token for the real validation handler.
     */
    internal string CreateToken(string? scopes = "catalogue.read catalogue.write", string? failure = null, string? claimType = null)
    {
        using var otherKey = RSA.Create(2048);
        var now = DateTime.UtcNow;
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new Claim("sub", failure == "empty-sub" ? " " : "test-traveller"));
        if (scopes is not null)
            foreach (var value in failure == "repeated-scopes" ? scopes.Split(' ') : new[] { scopes })
                claims.Add(new Claim(claimType ?? ScopeClaimType, value));
        var token = new JwtSecurityToken(
            failure == "issuer" ? "https://other.example.test" : Issuer,
            failure == "audience" ? "other-api" : Audience, claims,
            failure == "future" ? now.AddMinutes(5) : now.AddMinutes(-10),
            failure == "no-expiry" ? null : failure == "expired" ? now.AddMinutes(-1) : now.AddMinutes(10),
            failure == "unsigned" ? null : new SigningCredentials(
                new RsaSecurityKey(failure == "signature" ? otherKey : signingKey), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /**
     * Configures the test host.
     * @param builder The isolated host builder.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Authentication:Bearer:Authority"] = Issuer,
                ["Authentication:Bearer:Issuer"] = Issuer,
                ["Authentication:Bearer:Audience"] = Audience,
                ["Authentication:Bearer:ScopeClaimType"] = ScopeClaimType
            }));
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(signingKey.ExportParameters(false)));
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
            services.AddControllers().AddApplicationPart(typeof(IdentityProbeController).Assembly);
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    /**
     * Keeps existing resource tests authenticated through real signed tokens.
     * @param client The isolated client to configure.
     */
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
    }

    /**
     * Releases the test-only signing key after its host shuts down.
     * @param disposing Whether managed resources are being disposed.
     */
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) signingKey.Dispose();
    }
}
