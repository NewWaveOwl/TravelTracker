using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TravelTracker.Users.Api;

namespace TravelTracker.Users.Tests;

/**
 * Proves the Users host starts in Development with no other service running.
 * Users ships no defaults for its database or identity provider, so placeholder values are supplied per test.
 */
public sealed class UsersStartupTests
{
    const string Authority = "https://identity.example.invalid";

    /**
     * Builds a Development host whose configuration is the shipped settings plus the given overrides.
     * @param overrides Configuration keys added on top of the shipped settings.
     * @return A factory that starts the Users host without contacting any other service.
     */
    static WebApplicationFactory<UsersProgram> DevelopmentHost(Dictionary<string, string?> overrides) =>
        new WebApplicationFactory<UsersProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(overrides));
        });

    [Fact]
    public async Task Health_ReturnsOkWithNoDatabaseConfiguration()
    {
        // Bearer settings are still required at startup (ValidateOnStart); ConnectionStrings:Users is deliberately absent.
        using var factory = DevelopmentHost(new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Authority"] = Authority,
            ["Authentication:Bearer:Issuer"] = Authority,
            ["Authentication:Bearer:Audience"] = "traveltracker-api"
        });
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health");
        var body = await health.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("Users", body.GetProperty("service").GetString());
    }

    [Fact]
    public async Task Development_ProtectedProfileRejectsAnonymousCaller()
    {
        using var factory = DevelopmentHost(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Users"] = "Host=unused.test;Database=unused",
            ["Authentication:Bearer:Authority"] = Authority,
            ["Authentication:Bearer:Issuer"] = Authority,
            ["Authentication:Bearer:Audience"] = "traveltracker-api"
        });
        using var client = factory.CreateClient();

        using var profile = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    public async Task Health_RemainsPublicWithMalformedBearer()
    {
        using var factory = DevelopmentHost(new Dictionary<string, string?>
        {
            ["Authentication:Bearer:Authority"] = Authority,
            ["Authentication:Bearer:Issuer"] = Authority,
            ["Authentication:Bearer:Audience"] = "traveltracker-api"
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "M4_TOKEN_SENTINEL_malformed");

        using var health = await client.GetAsync("/health");
        var body = await health.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.DoesNotContain("M4_TOKEN_SENTINEL_malformed", body);
    }
}
