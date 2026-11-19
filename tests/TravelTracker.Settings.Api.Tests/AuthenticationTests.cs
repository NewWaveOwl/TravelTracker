using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TravelTracker.Settings.Api.Tests.Support;

namespace TravelTracker.Settings.Api.Tests;

/**
 * verifies explicit settings.read and settings.write policies and production JWT validation over HTTP.
 * these tests use real signed tokens; the Users stub only answers standing lookups.
 */
public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("settings.write", 403)]
    [InlineData("catalogue.read catalogue.write", 403)]
    [InlineData("settings.read", 200)]
    [InlineData("settings.read settings.write", 200)]
    public async Task Get_RequiresSettingsReadScope(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync("/api/v1/settings");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (status != 200) Assert.Equal(0, factory.Users!.Calls);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("settings.read", 403)]
    [InlineData("catalogue.write", 403)]
    [InlineData("settings.write", 200)]
    [InlineData("settings.read settings.write", 200)]
    public async Task Put_RequiresSettingsWriteScope(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.PutSettingsAsync(SettingsTestData.ValidPreferences(), "\"0\"");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        if (status != 200) Assert.Equal(0, factory.Users!.Calls);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    [InlineData("unsigned")]
    [InlineData("malformed")]
    [InlineData("no-expiry")]
    [InlineData("future")]
    public async Task ProtectedRoutes_RejectInvalidBearerTokensBeforeStandingLookup(string failure)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(failure: failure);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var read = await client.GetAsync("/api/v1/settings");
        using var write = await client.PutAsJsonAsync("/api/v1/settings", SettingsTestData.ValidPreferences());

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        Assert.Contains(read.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Contains(write.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.DoesNotContain(token, await read.Content.ReadAsStringAsync());
        Assert.DoesNotContain(token, await write.Content.ReadAsStringAsync());
        Assert.DoesNotContain(token, string.Join(" ", read.Headers.WwwAuthenticate));
        Assert.DoesNotContain(token, string.Join(" ", write.Headers.WwwAuthenticate));
        Assert.Equal(0, factory.Users!.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Health_IsAnonymousAndDoesNotCallUsers(bool invalidBearer)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        if (invalidBearer) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "M4_TOKEN_SENTINEL_malformed");

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Settings", body.GetProperty("service").GetString());
        Assert.Equal(0, factory.Users!.Calls);
    }
}
