using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using TravelTracker.Settings.Api.Access;
using TravelTracker.Settings.Data;

namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * creates an isolated Settings HTTP host with signed test tokens and a stubbed Users boundary.
 * without a connection string the host uses EF InMemory; with one it uses that PostgreSQL database and applies migrations.
 * @param connectionString an isolated PostgreSQL connection for restart tests, or null for InMemory.
 */
internal sealed class ApiFactory(string? connectionString = null) : WebApplicationFactory<SettingsProgram>
{
    internal const string Issuer = "https://issuer.example.test";
    internal const string Audience = "traveltracker-tests";
    internal const string ServiceToken = "test-service-token";

    private readonly RSA signingKey = RSA.Create(2048);
    private readonly string memoryDatabaseName = Guid.NewGuid().ToString("N");
    private HttpClient? discoveryClient;

    internal UsersBackchannelHandler? Users { get; private set; }
    internal DiscoveryBackchannelHandler? Discovery { get; private set; }

    /**
     * creates a JWT signed by the isolated test key; production still uses real issuer discovery.
     * @param scopes space-separated permissions, or null for no scope claim.
     * @param subject the validated user subject.
     * @param failure an optional invalid-token case.
     * @return a bearer token for this isolated API host.
     */
    internal string CreateToken(string? scopes = "settings.read settings.write", string subject = "alice", string? failure = null)
    {
        if (failure == "malformed") return "M4_TOKEN_SENTINEL_malformed";
        using var otherKey = RSA.Create(2048);
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new Claim("sub", failure == "empty-sub" ? " " : subject));
        if (!string.IsNullOrWhiteSpace(scopes)) claims.Add(new Claim("scope", scopes));
        var now = DateTime.UtcNow;
        var issuer = failure == "issuer" ? "https://other-issuer.example.test" : Issuer;
        var audience = failure == "audience" ? "other-api" : Audience;
        SigningCredentials? credentials = failure == "unsigned" ? null : new SigningCredentials(
            new RsaSecurityKey(failure == "signature" ? otherKey : signingKey) { KeyId = "settings-test-key" },
            SecurityAlgorithms.RsaSha256);
        DateTime? expires = failure == "no-expiry" ? null : failure == "expired" ? now.AddMinutes(-5) : now.AddMinutes(10);
        var notBefore = failure == "expired" ? now.AddMinutes(-10) : failure == "future" ? now.AddMinutes(5) : now.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore, expires, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /**
     * creates a client carrying the requested user's token.
     * @param subject the validated user subject.
     * @param scopes permissions to include, or null to send no bearer token.
     * @return an authenticated or anonymous test client.
     */
    internal HttpClient CreateClientFor(string subject = "alice", string? scopes = "settings.read settings.write")
    {
        var client = CreateClient();
        if (scopes is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(scopes, subject));
        return client;
    }

    /**
     * configures test authentication discovery, storage and the Users HTTP boundary stub.
     * @param builder the isolated host configuration.
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
            ["ConnectionStrings:Settings"] = connectionString ?? "Host=unused.test;Database=unused",
            ["Database:ApplyMigrations"] = (connectionString is not null).ToString(),
            ["Users:InternalBaseUrl"] = "https://users.example.test",
            ["Users:ServiceAccessToken"] = ServiceToken
        }));
        builder.ConfigureTestServices(services =>
        {
            if (connectionString is null)
            {
                services.RemoveAll<DbContextOptions<SettingsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<SettingsDbContext>>();
                services.AddDbContextFactory<SettingsDbContext>(options => options.UseInMemoryDatabase(memoryDatabaseName));
            }

            services.AddControllers().AddApplicationPart(typeof(FallbackProbeController).Assembly);
            Users = new UsersBackchannelHandler();
            services.AddHttpClient<UsersAccessClient>().ConfigurePrimaryHttpMessageHandler(() => Users);
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
     * releases the isolated discovery client and signing key after the host stops.
     * @param disposing whether managed resources are being disposed.
     */
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        discoveryClient?.Dispose();
        signingKey.Dispose();
    }
}
