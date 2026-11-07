using TravelTracker.Trips.Data.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TravelTracker.Data.Repositories;
using TravelTracker.Trips.Api;

namespace TravelTracker.Trips.Api.Tests.Support;

/**
 * Creates an isolated Trips HTTP host with real signed tokens, the production JWT configuration, a scripted
 * upstream for Users and the catalogues, and an owner-aware in-memory store.
 */
internal sealed class TripsApiFactory : WebApplicationFactory<TripsProgram>
{
    internal const string Issuer = "https://issuer.example.test";
    internal const string Audience = "traveltracker-tests";
    internal const string BackendCredential = "users-service-token";

    private readonly RSA signingKey = RSA.Create(2048);
    private HttpClient? discoveryClient;

    /** Scripted Users and catalogue behavior. */
    internal TripUpstream Upstream { get; } = new();

    /** Owner-aware store that the host writes to in tests. */
    internal InMemoryTripRepository Repository { get; } = new();

    /** Discovery backchannel that serves the test signing key. */
    internal DiscoveryBackchannelHandler? Discovery { get; private set; }

    /**
     * Creates a JWT signed by the isolated test key, or a deliberately invalid one for failure cases.
     *
     * @param scopes Space-separated permissions, or null for no scope claim.
     * @param subject Validated subject.
     * @param failure Optional invalid-token case.
     * @return A bearer token for this isolated host.
     */
    internal string CreateToken(string? scopes = "trips.read trips.write", string subject = "alice", string? failure = null)
    {
        using var otherKey = RSA.Create(2048);
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new Claim("sub", failure == "empty-sub" ? " " : subject));
        if (!string.IsNullOrWhiteSpace(scopes)) claims.Add(new Claim("scope", scopes));
        var now = DateTime.UtcNow;
        var issuer = failure == "issuer" ? "https://other-issuer.example.test" : Issuer;
        var audience = failure == "audience" ? "other-api" : Audience;
        SigningCredentials? credentials = failure == "unsigned" ? null : new SigningCredentials(
            new RsaSecurityKey(failure == "signature" ? otherKey : signingKey) { KeyId = "trips-test-key" },
            SecurityAlgorithms.RsaSha256);
        var expires = failure == "expired" ? now.AddMinutes(-5) : now.AddMinutes(10);
        var notBefore = failure == "expired" ? now.AddMinutes(-10) : now.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore, expires, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /**
     * Creates a client carrying the requested user token, or none when scopes is null.
     *
     * @param subject Validated subject.
     * @param scopes Permissions to include, or null to omit the bearer token.
     * @return An authenticated or anonymous client.
     */
    internal HttpClient CreateClientFor(string subject = "alice", string? scopes = "trips.read trips.write")
    {
        var client = CreateClient();
        if (scopes is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(scopes, subject));
        return client;
    }

    /**
     * Applies the test authority, the scripted upstream and the owner-aware store to the production host.
     *
     * @param builder The isolated host configuration.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Authority"] = Issuer,
            ["Authentication:Bearer:Issuer"] = Issuer,
            ["Authentication:Bearer:Audience"] = Audience,
            ["Authentication:Bearer:ScopeClaimType"] = "scope",
            ["Authentication:Bearer:ClockSkewSeconds"] = "0",
            ["Authentication:Bearer:ValidAlgorithms:0"] = SecurityAlgorithms.RsaSha256,
            ["ConnectionStrings:Trips"] = "Host=unused.test;Database=unused",
            ["Database:ApplyMigrations"] = "false",
            ["TripReferences:AccommodationsBaseUrl"] = "https://upstream.example.test/",
            ["TripReferences:AttractionsBaseUrl"] = "https://upstream.example.test/",
            ["TripReferences:TransportBaseUrl"] = "https://upstream.example.test/",
            ["TripReferences:UsersBaseUrl"] = "https://upstream.example.test/",
            ["TripReferences:UsersServiceAccessToken"] = BackendCredential,
            ["TripReferences:Timeout"] = "00:00:05"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITripRepository>();
            services.AddSingleton<ITripRepository>(Repository);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Upstream));
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                Discovery = new DiscoveryBackchannelHandler(signingKey.ExportParameters(false), Issuer);
                discoveryClient = new HttpClient(Discovery);
                options.Backchannel = discoveryClient;
            });
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    /**
     * Releases the discovery client and signing key after stopping the host.
     *
     * @param disposing Whether managed resources are being disposed.
     */
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        discoveryClient?.Dispose();
        signingKey.Dispose();
    }
}
