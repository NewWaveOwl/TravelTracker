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
using TravelTracker.Accommodations.Api.Access;
using TravelTracker.Accommodations.Data;

namespace TravelTracker.Accommodations.Api.Tests.Support;

/** Creates isolated Accommodations HTTP hosts with signed user tokens and stubbed Users access. */
internal sealed class ApiFactory : WebApplicationFactory<TravelTracker.Accommodations.Api.AccommodationsProgram>
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
     * Creates a JWT signed by the isolated test key; production still uses real issuer discovery.
     * @param scopes Space-separated permissions, or null for no scope claim.
     * @param subject Validated user subject.
     * @param failure Optional invalid-token case.
     * @return A bearer token for this isolated API host.
     */
    internal string CreateToken(string? scopes = "catalogue.read catalogue.write", string subject = "alice", string? failure = null)
    {
        using var otherKey = RSA.Create(2048);
        var claims = new List<Claim>();
        if (failure != "missing-sub") claims.Add(new Claim("sub", failure == "empty-sub" ? " " : subject));
        if (!string.IsNullOrWhiteSpace(scopes)) claims.Add(new Claim("scope", scopes));
        var now = DateTime.UtcNow;
        var issuer = failure == "issuer" ? "https://other-issuer.example.test" : Issuer;
        var audience = failure == "audience" ? "other-api" : Audience;
        SigningCredentials? credentials = failure == "unsigned" ? null : new SigningCredentials(
            new RsaSecurityKey(failure == "signature" ? otherKey : signingKey) { KeyId = "accommodations-test-key" },
            SecurityAlgorithms.RsaSha256);
        var expires = failure == "expired" ? now.AddMinutes(-5) : now.AddMinutes(10);
        var token = new JwtSecurityToken(issuer, audience, claims, now.AddMinutes(-1), expires, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /**
     * Creates a client with the requested user token.
     * @param subject Validated user subject.
     * @param scopes Permissions to include, or null to omit the bearer token.
     * @return An authenticated or anonymous test client.
     */
    internal HttpClient CreateClientFor(string subject = "alice", string? scopes = "catalogue.read catalogue.write")
    {
        var client = CreateClient();
        if (scopes is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(scopes, subject));
        return client;
    }

    /**
     * Configures test authentication discovery, PostgreSQL replacement and the Users HTTP boundary stub.
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
            ["ConnectionStrings:Accommodations"] = "Host=unused.test;Database=unused",
            ["Database:ApplyMigrations"] = "false",
            ["Users:InternalBaseUrl"] = "https://users.example.test",
            ["Users:ServiceAccessToken"] = ServiceToken
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AccommodationsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AccommodationsDbContext>>();
            services.AddDbContextFactory<AccommodationsDbContext>(options => options.UseInMemoryDatabase(memoryDatabaseName));

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
     * Releases the isolated discovery client and signing key after stopping the host.
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
