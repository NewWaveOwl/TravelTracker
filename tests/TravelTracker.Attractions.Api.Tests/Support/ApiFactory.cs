using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using TravelTracker.Attractions.Api.Access;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Creates an isolated in-memory HTTP host for each test.
 */
internal sealed class ApiFactory : WebApplicationFactory<TravelTracker.Attractions.Api.AttractionsProgram>
{
    private readonly PostgresTestDatabase? ownedDatabase;
    private readonly string databaseConnection;
    internal bool UsersUnavailable { get; init; }
    internal bool SeedExamples { get; init; } = true;
    internal UsersBackchannelHandler? Users { get; private set; }

    /**
     * Creates a fresh store, or reuses an explicitly owned store for restart tests.
     * @param connectionString The retained test database, or null for a new isolated database.
     */
    internal ApiFactory(string? connectionString = null)
    {
        if (connectionString is null) ownedDatabase = new PostgresTestDatabase();
        databaseConnection = connectionString ?? ownedDatabase!.ConnectionString;
    }

    /**
     * Supplies validated identity for direct service regression tests only.
     * @param provider The isolated service scope.
     */
    internal void PrepareScope(IServiceProvider provider)
    {
        var caller = provider.GetRequiredService<CallerContext>();
        caller.Issuer = Issuer;
        caller.Subject = "test-traveller";
        caller.IsAdministrator = true;
    }
    internal const string Issuer = "https://issuer.example.test";
    internal const string Audience = "traveltracker-tests";
    private readonly RSA signingKey = RSA.Create(2048);
    internal string ScopeClaimType { get; init; } = "scope";
    internal int ClockSkewSeconds { get; init; } = 30;
    internal string[] ValidAlgorithms { get; init; } = ["RS256"];
    internal string MetadataIssuer { get; init; } = Issuer;
    internal string ExpectedIssuer { get; init; } = Issuer;
    internal DiscoveryBackchannelHandler? Discovery { get; private set; }
    private HttpClient? backchannel;

    /**
     * Issues a test-only RSA-signed token; production still uses provider discovery.
     * @param scopes The permissions to issue, or null for no scope claim.
     * @param failure An optional invalid-token scenario.
     * @param claimType An optional scope claim name override.
     * @param lifetimeOffsetSeconds An optional expiration offset to exercise clock tolerance.
     * @return A serialized bearer token for the real validation handler.
     */
    internal string CreateToken(string? scopes = "catalogue.read catalogue.write", string? failure = null,
        string? claimType = null, int? lifetimeOffsetSeconds = null, string subject = "test-traveller")
    {
        using var otherKey = RSA.Create(2048);
        var now = DateTime.UtcNow;
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new Claim("sub", failure == "empty-sub" ? " " : subject));
        if (scopes is not null)
            foreach (var value in failure == "repeated-scopes" ? scopes.Split(' ') : new[] { scopes })
                claims.Add(new Claim(claimType ?? ScopeClaimType, value));
        var token = new JwtSecurityToken(
            failure == "issuer" ? "https://other.example.test" : ExpectedIssuer,
            failure == "audience" ? "other-api" : Audience, claims,
            failure == "future" ? now.AddMinutes(5) : now.AddMinutes(-10),
            lifetimeOffsetSeconds is not null ? now.AddSeconds(lifetimeOffsetSeconds.Value) :
                failure == "no-expiry" ? null : failure == "expired" ? now.AddMinutes(-5) : now.AddMinutes(10),
            failure == "unsigned" ? null : new SigningCredentials(
                new RsaSecurityKey(failure == "signature" ? otherKey : signingKey) { KeyId = "test-signing-key" },
                failure == "algorithm" ? SecurityAlgorithms.RsaSha512 : SecurityAlgorithms.RsaSha256));
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
                ["ConnectionStrings:Attractions"] = databaseConnection,
                ["Database:ApplyMigrations"] = "true",
                ["Database:SeedExamples"] = SeedExamples.ToString(),
                ["Users:AccessEndpoint"] = "https://users.example.test/api/v1/users/resolve-access",
                ["Users:ServiceAccessToken"] = "test-service-token",
                ["Authentication:Bearer:Issuer"] = ExpectedIssuer,
                ["Authentication:Bearer:Audience"] = Audience,
                ["Authentication:Bearer:ScopeClaimType"] = ScopeClaimType,
                ["Authentication:Bearer:ClockSkewSeconds"] = ClockSkewSeconds.ToString(),
                ["Authentication:Bearer:ValidAlgorithms:0"] = ValidAlgorithms[0]
            }));
        builder.ConfigureTestServices(services =>
        {
            Users = new UsersBackchannelHandler(UsersUnavailable);
            services.AddHttpClient<UsersAccessClient>().ConfigurePrimaryHttpMessageHandler(() => Users);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                Discovery = new DiscoveryBackchannelHandler(signingKey.ExportParameters(false), MetadataIssuer);
                backchannel = new HttpClient(Discovery);
                options.Backchannel = backchannel;
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
        if (disposing)
        {
            backchannel?.Dispose();
            signingKey.Dispose();
            ownedDatabase?.Dispose();
        }
    }
}
