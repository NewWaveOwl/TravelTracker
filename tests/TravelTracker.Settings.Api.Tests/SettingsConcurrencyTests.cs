using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Settings.Api.Tests.Support;

namespace TravelTracker.Settings.Api.Tests;

/**
 * verifies the ETag and If-Match contract: revisions, 428 for a missing precondition, 412 for stale or malformed ones, and no partial writes.
 */
public sealed class SettingsConcurrencyTests
{
    [Fact]
    public async Task Get_BeforeFirstSave_ReturnsRevisionZeroEtag()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.GetAsync("/api/v1/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"0\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_WithoutIfMatch_Returns428AndSavesNothing()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(ValidPreferences(), null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var get = await client.GetAsync("/api/v1/settings");
        Assert.Equal("\"0\"", get.Headers.ETag?.Tag);
        Assert.Equal("system", (await get.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("theme").GetString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("W/\"0\"")]
    [InlineData("*")]
    [InlineData("\"abc\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"\"")]
    public async Task Put_WithMalformedOrWeakIfMatch_Returns412AndSavesNothing(string ifMatch)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(ValidPreferences(), ifMatch);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        using var get = await client.GetAsync("/api/v1/settings");
        Assert.Equal("\"0\"", get.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_FirstSaveWithRevisionZero_SavesAndReturnsRevisionOne()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(ValidPreferences(), "\"0\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
        using var get = await client.GetAsync("/api/v1/settings");
        Assert.Equal("\"1\"", get.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_WithCurrentEtag_SavesAndAdvancesTheEtag()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        using var read = await client.GetAsync("/api/v1/settings");

        using var response = await client.PutSettingsAsync(ValidPreferences(), read.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(read.Headers.ETag.Tag, response.Headers.ETag?.Tag);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_WithStaleEtag_Returns412AndKeepsThePreviousSave()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        using var read = await client.GetAsync("/api/v1/settings");
        var staleTag = read.Headers.ETag!.Tag;
        using var first = await client.PutSettingsAsync(ValidPreferences(), staleTag);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PutSettingsAsync(new
        {
            theme = "light",
            defaultMapLatitude = 0,
            defaultMapLongitude = 0,
            defaultMapZoom = 2,
            preferredTransportMode = "bike",
            timeZone = "UTC"
        }, staleTag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
        using var get = await client.GetAsync("/api/v1/settings");
        var settings = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dark", settings.GetProperty("theme").GetString());
        Assert.Equal("train", settings.GetProperty("preferredTransportMode").GetString());
        Assert.Equal("\"1\"", get.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task TwoTabs_LaterSaveWithTheSameEtagIsRefused()
    {
        using var factory = new ApiFactory();
        using var tabA = factory.CreateClientFor("alice");
        using var tabB = factory.CreateClientFor("alice");
        using var readA = await tabA.GetAsync("/api/v1/settings");
        using var readB = await tabB.GetAsync("/api/v1/settings");

        using var savedA = await tabA.PutSettingsAsync(ValidPreferences(), readA.Headers.ETag!.Tag);
        using var savedB = await tabB.PutSettingsAsync(new
        {
            theme = "light",
            defaultMapLatitude = 1,
            defaultMapLongitude = 1,
            defaultMapZoom = 1,
            preferredTransportMode = "car",
            timeZone = "UTC"
        }, readB.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.OK, savedA.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, savedB.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidValuesWithCurrentEtag_Returns400AndKeepsTheRevision()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        using var read = await client.GetAsync("/api/v1/settings");

        using var response = await client.PutSettingsAsync(new
        {
            theme = "neon",
            defaultMapLatitude = 0,
            defaultMapLongitude = 0,
            defaultMapZoom = 2,
            preferredTransportMode = "car",
            timeZone = "UTC"
        }, read.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var get = await client.GetAsync("/api/v1/settings");
        Assert.Equal(read.Headers.ETag.Tag, get.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_InvalidValuesWithStaleEtag_Returns400BeforeTheRevisionCheck()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(new
        {
            theme = "neon",
            defaultMapLatitude = 0,
            defaultMapLongitude = 0,
            defaultMapZoom = 2,
            preferredTransportMode = "car",
            timeZone = "UTC"
        }, "\"42\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_MissingBodyFieldWithoutIfMatch_Returns400BeforeThePreconditionCheck()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(new { theme = "dark" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /**
     * the complete valid preference set used by the precondition tests.
     * @return a JSON-serializable preference object.
     */
    private static object ValidPreferences() => new
    {
        theme = "dark",
        defaultMapLatitude = 45.5m,
        defaultMapLongitude = -73.5m,
        defaultMapZoom = 12,
        preferredTransportMode = "train",
        timeZone = "America/Montreal"
    };
}
