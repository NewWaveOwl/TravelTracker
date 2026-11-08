using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Settings.Api.Tests.Support;

namespace TravelTracker.Settings.Api.Tests;

/**
 * proves saved preferences survive a complete host restart on the real PostgreSQL provider.
 */
[Trait("Category", "PostgreSQL")]
public sealed class SettingsPostgreSqlRestartTests
{
    [Fact]
    public async Task SavedPreferences_SurviveHostRestart_AndRejectedUpdatesKeepThem()
    {
        using var database = new PostgresTestDatabase();
        using (var firstHost = new ApiFactory(database.ConnectionString))
        using (var client = firstHost.CreateClientFor("alice"))
        {
            using var saved = await client.PutSettingsAsync(new
            {
                theme = "dark",
                defaultMapLatitude = 45.25m,
                defaultMapLongitude = -73.75m,
                defaultMapZoom = 12,
                preferredTransportMode = "train",
                timeZone = "America/Montreal"
            });
            using var rejected = await client.PutSettingsAsync(new
            {
                theme = "neon",
                defaultMapLatitude = 45.25m,
                defaultMapLongitude = -73.75m,
                defaultMapZoom = 12,
                preferredTransportMode = "train",
                timeZone = "America/Montreal"
            });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using var restartedHost = new ApiFactory(database.ConnectionString);
        using var restarted = restartedHost.CreateClientFor("alice");
        using var response = await restarted.GetAsync("/api/v1/settings");
        var settings = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dark", settings.GetProperty("theme").GetString());
        Assert.Equal(45.25m, settings.GetProperty("defaultMapLatitude").GetDecimal());
        Assert.Equal(-73.75m, settings.GetProperty("defaultMapLongitude").GetDecimal());
        Assert.Equal(12, settings.GetProperty("defaultMapZoom").GetInt32());
        Assert.Equal("train", settings.GetProperty("preferredTransportMode").GetString());
        Assert.Equal("America/Montreal", settings.GetProperty("timeZone").GetString());
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task AccountsWithoutSavedPreferences_ReadDefaultsAfterRestart()
    {
        using var database = new PostgresTestDatabase();
        using (var firstHost = new ApiFactory(database.ConnectionString))
        using (var client = firstHost.CreateClientFor("alice"))
        {
            using var saved = await client.PutSettingsAsync(new
            {
                theme = "light",
                defaultMapLatitude = 1,
                defaultMapLongitude = 1,
                defaultMapZoom = 4,
                preferredTransportMode = "bus",
                timeZone = "UTC"
            });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }

        using var restartedHost = new ApiFactory(database.ConnectionString);
        using var bob = restartedHost.CreateClientFor("bob");
        using var response = await bob.GetAsync("/api/v1/settings");
        var settings = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("system", settings.GetProperty("theme").GetString());
        Assert.Equal("car", settings.GetProperty("preferredTransportMode").GetString());
    }
}
