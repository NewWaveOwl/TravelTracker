using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Data.SeedData;
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

namespace TravelTracker.Transport.Api.Tests;

/**
 * Creates an isolated in-memory HTTP host for each test.
 */
internal sealed class ApiFactory : WebApplicationFactory<TravelTracker.Transport.Api.TransportProgram>
{
    private readonly string databaseName = Guid.NewGuid().ToString();
    internal string? PostgresConnection { get; init; }
    internal bool SeedExamples { get; init; } = true;
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
        string? claimType = null, int? lifetimeOffsetSeconds = null)
    {
        using var otherKey = RSA.Create(2048);
        var now = DateTime.UtcNow;
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new Claim("sub", failure == "empty-sub" ? " " : "test-traveller"));
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
                ["ConnectionStrings:Transport"] = PostgresConnection ?? "Host=localhost;Database=unused",
                ["Database:ApplyMigrations"] = (PostgresConnection is not null).ToString(),
                ["Database:SeedExamples"] = SeedExamples.ToString(),
                ["Authentication:Bearer:Authority"] = Issuer,
                ["Authentication:Bearer:Issuer"] = ExpectedIssuer,
                ["Authentication:Bearer:Audience"] = Audience,
                ["Authentication:Bearer:ScopeClaimType"] = ScopeClaimType,
                ["Authentication:Bearer:ClockSkewSeconds"] = ClockSkewSeconds.ToString(),
                ["Authentication:Bearer:ValidAlgorithms:0"] = ValidAlgorithms[0]
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<TransportDbContext>>();
            services.RemoveAll<IDbContextFactory<TransportDbContext>>();
            services.RemoveAll<TransportDbContext>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<TransportDbContext>>();
            services.AddDbContextFactory<TransportDbContext>(options => {
                if (PostgresConnection is null) options.UseInMemoryDatabase(databaseName);
                else options.UseNpgsql(PostgresConnection);
            });
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
        if (PostgresConnection is not null || !SeedExamples) {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
            return;
        }
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TransportDbContext>();
        database.Database.EnsureCreated();
        if (!database.TransportModes.Any())
            foreach (var record in TransportSeedData.Transports()) new EfTransportModeRepository(database).AddAsync(record).GetAwaiter().GetResult();
        if (!database.RoutePoints.Any())
            foreach (var record in TransportSeedData.Points()) new EfRoutePointRepository(database).AddAsync(record).GetAwaiter().GetResult();
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
        }
    }
}
