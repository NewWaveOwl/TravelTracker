using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TravelTracker.Users.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TravelTracker.Users.Api;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Tests.Support;

/** Test-only Users host with production JWT validation, deterministic userinfo and optional isolated PostgreSQL. */
public sealed class UsersApiFactory : WebApplicationFactory<UsersProgram>
{
    public const string Issuer = "https://issuer.example.test/";
    public const string Audience = "traveltracker-tests";
    public const string Secret = "test-bootstrap-credential-at-least-32-characters";
    public const string ServiceToken = "test-independent-service-credential-at-least-32";
    public const string ServiceSubject = "attractions-service";
    public const string Email = "initial-admin@example.test";
    readonly RSA key = RSA.Create(2048);
    public MemoryUserRepository Repository { get; } = new();
    public UserInfoHandler Identity { get; } = new();
    public UsersLogCapture Logs { get; } = new();
    public string ConfiguredSecret { get; set; } = Secret;
    public string? ProfileDatabaseConnection { get; init; }
    public IInterceptor? ProfileSaveInterceptor { get; init; }

    /**
     * Starts the actual middleware with an isolated signing key and HTTP dependencies.
     * @param builder Test host builder.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Users"] = ProfileDatabaseConnection ?? "Host=unused.test;Database=unused",
            ["Authentication:Bearer:Authority"] = Issuer,
            ["Authentication:Bearer:Issuer"] = Issuer,
            ["Authentication:Bearer:Audience"] = Audience,
            ["Authentication:Bearer:ClockSkewSeconds"] = "0",
            ["Bootstrap:Email"] = Email,
            ["Bootstrap:Issuer"] = Issuer,
            ["Bootstrap:Secret"] = ConfiguredSecret,
            ["UsersAccess:ServiceToken"] = ServiceToken,
            ["UsersAccess:AllowedServiceSubjects:0"] = ServiceSubject,
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(Repository);
            if (ProfileDatabaseConnection is null)
            {
                services.RemoveAll<TravelTracker.Users.Data.Repositories.Profile.IUserRepository>();
                services.AddSingleton<TravelTracker.Users.Data.Repositories.Profile.IUserRepository>(Repository);
            }
            else if (ProfileSaveInterceptor is { } interceptor)
            {
                services.ConfigureDbContext<UsersDbContext>(options => options.AddInterceptors(interceptor));
            }
            services.AddHttpClient<VerifiedIdentityClient>().ConfigurePrimaryHttpMessageHandler(() => Identity);
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(key) { KeyId = "test-key" });
                options.Configuration = metadata;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        });
    }

    /**
     * Creates a JWT passing through the real JwtBearer signature/issuer/audience/expiry checks.
     * @param failure Optional rejection scenario.
     * @param subject Validated subject for the token.
     * @param scope Granted scope.
     * @return Signed test-only bearer token.
     */
    public string Token(string? failure = null, string subject = "admin-subject", string scope = "catalogue.read users.read", string? role = null)
    {
        if (failure == "malformed") return "M4_TOKEN_SENTINEL_malformed";
        using var other = RSA.Create(2048);
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new("scope", scope) };
        if (failure != "missing-sub") claims.Add(new("sub", failure == "empty-sub" ? " " : subject));
        if (role is not null) claims.Add(new Claim("role", role));
        var expires = failure == "no-expiry" ? (DateTime?)null : failure == "expired" ? now.AddMinutes(-1) : now.AddMinutes(failure == "future" ? 15 : 5);
        var notBefore = failure == "future" ? now.AddMinutes(5) : now.AddMinutes(-10);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            failure == "issuer" ? "https://wrong.example.test/" : Issuer,
            failure == "audience" ? "wrong-audience" : Audience,
            claims, notBefore, expires,
            failure == "unsigned" ? null : new SigningCredentials(new RsaSecurityKey(failure == "signature" ? other : key) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256)));
    }

    public HttpClient Browser(string? token = null, bool https = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"), AllowAutoRedirect = false });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) key.Dispose(); }
}
