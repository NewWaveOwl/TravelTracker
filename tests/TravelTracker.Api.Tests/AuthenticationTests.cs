using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TravelTracker.Api.Authentication;
using Swashbuckle.AspNetCore.Swagger;

namespace TravelTracker.Api.Tests;

/**
 * Exercises production JWT validation and scope policies with real signed tokens.
 */
public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("Authority", "")]
    [InlineData("Authority", "http://issuer.example.test")]
    [InlineData("Issuer", "")]
    [InlineData("Issuer", "http://issuer.example.test")]
    [InlineData("Audience", "")]
    [InlineData("ScopeClaimType", " ")]
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
