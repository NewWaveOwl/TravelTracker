using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelTracker.Api.Authentication;
using Swashbuckle.AspNetCore.Swagger;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Api.Tests;

/**
 * Exercises production JWT validation and scope policies with real signed tokens.
 */
public sealed class AuthenticationTests
{
    [Fact]
    public async Task PostValidation_MetadataFailureLogsSafeDiagnosticAndRejectsIdentity()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Authority"] = ApiFactory.Issuer,
            ["Authentication:Bearer:Issuer"] = ApiFactory.Issuer,
            ["Authentication:Bearer:Audience"] = ApiFactory.Audience
        }).Build();
        using var logs = new AuthenticationLogCapture();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders().AddProvider(logs));
        services.AddApiBearerAuthentication(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        options.ConfigurationManager = new UnavailableConfigurationManager();
        var context = new TokenValidatedContext(new DefaultHttpContext { RequestServices = provider },
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), options);

        await options.Events.OnTokenValidated(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.Contains(logs.Messages, message => message.Contains("discovery metadata retrieval failed (HttpRequestException)"));
        Assert.DoesNotContain(logs.Messages, message => message.Contains("sensitive-test-marker"));
        Assert.Empty(logs.Exceptions);
        Assert.DoesNotContain("sensitive-test-marker", context.Result.Failure.Message);
    }

    [Theory]
    [InlineData("Authority", "")]
    [InlineData("Authority", "http://issuer.example.test")]
    [InlineData("Issuer", "")]
    [InlineData("Issuer", "http://issuer.example.test")]
    [InlineData("Audience", "")]
    [InlineData("ScopeClaimType", " ")]
    [InlineData("ClockSkewSeconds", "-1")]
    [InlineData("ClockSkewSeconds", "121")]
    [InlineData("ValidAlgorithms:0", "HS256")]
    [InlineData("ValidAlgorithms:0", "none")]
    [InlineData("ValidAlgorithms:0", "")]
    public void Configuration_RejectsMissingOrInsecureSettings(string setting, string value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Authority"] = ApiFactory.Issuer,
            ["Authentication:Bearer:Issuer"] = ApiFactory.Issuer,
            ["Authentication:Bearer:Audience"] = ApiFactory.Audience,
            ["Authentication:Bearer:ScopeClaimType"] = "scope"
        };
        settings[$"Authentication:Bearer:{setting}"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiBearerAuthentication(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BearerSettings>>().Value);
    }

    [Fact]
    public async Task Development_ShippedConfigurationStartsWithPublicHealthAndProtectedCatalogue()
    {
        // No ApiFactory configuration overrides: load the repository's actual Development settings.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health");
        using var catalogue = await client.GetAsync("/api/v1/attractions");
        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, catalogue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public void Production_MissingProviderConfigurationStillFailsStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:Bearer:Authority"] = "",
                    ["Authentication:Bearer:Issuer"] = "",
                    ["Authentication:Bearer:Audience"] = ""
                }));
        });
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Theory]
    [InlineData("fallback", "missing-token", 401)]
    [InlineData("fallback", null, 403)]
    [InlineData("fallback", "catalogue.write", 403)]
    [InlineData("fallback", "catalogue.read", 200)]
    [InlineData("default", "missing-token", 401)]
    [InlineData("default", null, 403)]
    [InlineData("default", "catalogue.write", 403)]
    [InlineData("default", "catalogue.read", 200)]
    public async Task ImplicitPolicy_RequiresReadScope(string policy, string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = scopes == "missing-token" ? null : new("Bearer", factory.CreateToken(scopes));
        using var response = await client.GetAsync($"/test/identity/{policy}");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData(30, -10, 200)]
    [InlineData(30, -120, 401)]
    [InlineData(0, -10, 401)]
    [InlineData(30, 120, 200)]
    public async Task Lifetime_HonorsConfiguredBoundedTolerance(int skew, int expiryOffset, int status)
    {
        using var factory = new ApiFactory { ClockSkewSeconds = skew };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(lifetimeOffsetSeconds: expiryOffset));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData("RS256", "algorithm", 401)]
    [InlineData("RS512", "algorithm", 200)]
    public async Task Signature_RequiresAnExplicitlyAllowedAlgorithm(string algorithm, string failure, int status)
    {
        using var factory = new ApiFactory { ValidAlgorithms = [algorithm] };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(failure: failure));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_RetrievesConfiguredAuthorityAndPublicSigningKeys()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(factory.Discovery);
        Assert.Equal(new[] { $"{ApiFactory.Issuer}/.well-known/openid-configuration", $"{ApiFactory.Issuer}/keys" },
            factory.Discovery.RequestedUrls);
    }

    [Theory]
    [InlineData("https://issuer.example.test/", 401)]
    [InlineData("https://issuer.example.test/v2", 401)]
    public async Task Discovery_RejectsConfiguredIssuerMismatch(string metadataIssuer, int status)
    {
        using var factory = new ApiFactory { MetadataIssuer = metadataIssuer };
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.DoesNotContain(response.Headers.WwwAuthenticate, header => header.Parameter?.Contains("error_description") == true);
    }

    [Fact]
    public async Task Discovery_AllowsDistinctAuthorityWhenMetadataAndTokenMatchExpectedIssuer()
    {
        const string expected = "https://tokens.example.test/v2";
        using var factory = new ApiFactory { ExpectedIssuer = expected, MetadataIssuer = expected };
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("fallback")]
    [InlineData("default")]
    public void OpenApi_DescribesImplicitReadPermission(string policy)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var document = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = document.Paths[$"/test/identity/{policy}"].Operations![HttpMethod.Get];
        Assert.Contains("Required permission: catalogue.read.", operation.Description);
        Assert.True(operation.Responses!.ContainsKey("401"));
        Assert.True(operation.Responses.ContainsKey("403"));
    }

    [Fact]
    public void OpenApi_DescribesProtectedOperationsAndPublicHealth()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var document = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.NotNull(document.Components?.SecuritySchemes);
        Assert.True(document.Components.SecuritySchemes.ContainsKey("Bearer"));
        foreach (var (path, item) in document.Paths.Where(entry => entry.Key.StartsWith("/api/v1/", StringComparison.Ordinal)))
        {
            Assert.NotNull(item.Operations);
            foreach (var (method, operation) in item.Operations)
            {
                Assert.NotNull(operation.Security);
                Assert.Single(operation.Security);
                Assert.NotNull(operation.Responses);
                Assert.True(operation.Responses.ContainsKey("401"), path);
                Assert.True(operation.Responses.ContainsKey("403"), path);
                Assert.Contains(method == HttpMethod.Get ? "catalogue.read" : "catalogue.write", operation.Description);
            }
        }
        var health = document.Paths["/health"].Operations![HttpMethod.Get];
        Assert.True(health.Security is null || health.Security.Count == 0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("unsigned")]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    [InlineData("no-expiry")]
    [InlineData("future")]
    [InlineData("malformed")]
    public async Task InvalidBearer_Returns401(string failure)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = failure == "missing" ? null :
            new AuthenticationHeaderValue("Bearer", failure == "malformed" ? "not-a-jwt" : factory.CreateToken(failure: failure));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Theory]
    [InlineData(null, 403)]
    [InlineData("trips.read", 403)]
    [InlineData("catalogue.write", 403)]
    [InlineData("catalogue.read.extra", 403)]
    [InlineData("CATALOGUE.READ", 403)]
    [InlineData("catalogue.read,catalogue.write", 403)]
    [InlineData("catalogue.read", 200)]
    [InlineData("trips.read catalogue.read catalogue.write", 200)]
    public async Task Read_RequiresExactReadScope(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(scopes));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData("scope", "catalogue.read", 403)]
    [InlineData("scp", "catalogue.read", 200)]
    public async Task Read_UsesOnlyConfiguredScopeClaim(string claimType, string scopes, int status)
    {
        using var factory = new ApiFactory { ScopeClaimType = "scp" };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(scopes, claimType: claimType));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Fact]
    public async Task Read_AcceptsRepeatedScopeClaims()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            factory.CreateToken("trips.read catalogue.read", failure: "repeated-scopes"));
        using var response = await client.GetAsync("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/attractions", "catalogue.write")]
    [InlineData("GET", "/api/v1/attractions/11111111-1111-1111-1111-111111111111", "catalogue.write")]
    [InlineData("GET", "/api/v1/transport-modes/00000000-0000-0000-0000-000000000101", "catalogue.write")]
    [InlineData("GET", "/api/v1/route-points/00000000-0000-0000-0000-000000000201", "catalogue.write")]
    [InlineData("POST", "/api/v1/attractions", "catalogue.read")]
    [InlineData("PATCH", "/api/v1/attractions/11111111-1111-1111-1111-111111111111", "catalogue.read")]
    [InlineData("POST", "/api/v1/transport-modes", "catalogue.read")]
    [InlineData("POST", "/api/v1/route-points", "catalogue.read")]
    public async Task ResourceOperation_RejectsOppositePermission(string method, string path, string scopes)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(scopes));
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { name = "Must not save" })
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/attractions")]
    [InlineData("GET", "/api/v1/attractions/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/v1/attractions")]
    [InlineData("PATCH", "/api/v1/attractions/11111111-1111-1111-1111-111111111111")]
    [InlineData("GET", "/api/v1/transport-modes/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/v1/transport-modes")]
    [InlineData("GET", "/api/v1/route-points/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/v1/route-points")]
    public async Task ResourceOperation_RejectsAnonymousCaller(string method, string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("catalogue.read", 403)]
    [InlineData("catalogue.write", 201)]
    public async Task Create_RequiresWriteScopeAndRejectedWriteDoesNotPersist(string scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(scopes));
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "Permission test", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken("catalogue.read"));
        var records = await client.GetFromJsonAsync<JsonElement>("/api/v1/attractions?name=Permission%20test");
        Assert.Equal(status == 201 ? 1 : 0, records.GetArrayLength());
    }

    [Fact]
    public async Task Identity_ComesFromValidatedSubjectDespiteIdentityHeaderAndQuery()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "someone-else");
        var identity = await client.GetFromJsonAsync<JsonElement>("/test/identity?userId=someone-else");
        Assert.Equal("test-traveller", identity.GetProperty("subject").GetString());
        Assert.Equal(ApiFactory.Issuer, identity.GetProperty("issuer").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Health_RemainsPublicWithMissingOrInvalidToken(bool invalidToken)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = invalidToken ? new("Bearer", "invalid") : null;
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
