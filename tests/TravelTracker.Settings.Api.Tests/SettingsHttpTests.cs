using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TravelTracker.Settings.Api.Tests.Support;

namespace TravelTracker.Settings.Api.Tests;

/**
 * verifies the own-settings contract: defaults, round trips, field-level rejection, ownership and live standing.
 */
public sealed class SettingsHttpTests
{
    [Fact]
    public async Task Get_BeforeFirstSave_ReturnsDocumentedDefaults()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.GetAsync("/api/v1/settings");
        var settings = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("system", settings.GetProperty("theme").GetString());
        Assert.Equal(0, settings.GetProperty("defaultMapLatitude").GetDecimal());
        Assert.Equal(0, settings.GetProperty("defaultMapLongitude").GetDecimal());
        Assert.Equal(2, settings.GetProperty("defaultMapZoom").GetInt32());
        Assert.Equal("car", settings.GetProperty("preferredTransportMode").GetString());
        Assert.Equal("UTC", settings.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Put_ValidPreferences_ReturnsSavedValuesAndRoundTripsThroughGet()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var put = await client.PutSettingsAsync(ValidPreferences());
        using var get = await client.GetAsync("/api/v1/settings");

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        AssertPreferences(await put.Content.ReadFromJsonAsync<JsonElement>(), ValidPreferences());
        AssertPreferences(await get.Content.ReadFromJsonAsync<JsonElement>(), ValidPreferences());
    }

    [Fact]
    public async Task Put_ReplacesEveryFieldOfThePreviousSave()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        await client.PutSettingsAsync(ValidPreferences());

        using var put = await client.PutSettingsAsync(new
        {
            theme = "light",
            defaultMapLatitude = 0,
            defaultMapLongitude = 0,
            defaultMapZoom = 2,
            preferredTransportMode = "bike",
            timeZone = "UTC"
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        AssertPreferences(await put.Content.ReadFromJsonAsync<JsonElement>(), new
        {
            theme = "light",
            defaultMapLatitude = 0m,
            defaultMapLongitude = 0m,
            defaultMapZoom = 2,
            preferredTransportMode = "bike",
            timeZone = "UTC"
        });
    }

    [Theory]
    [InlineData(-90, -180, 0, "plane", "UTC")]
    [InlineData(90, 180, 20, "bike", "Europe/Paris")]
    public async Task Put_BoundaryValues_AreAccepted(decimal latitude, decimal longitude, int zoom, string mode, string timeZone)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(new
        {
            theme = "system",
            defaultMapLatitude = latitude,
            defaultMapLongitude = longitude,
            defaultMapZoom = zoom,
            preferredTransportMode = mode,
            timeZone
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(latitude, saved.GetProperty("defaultMapLatitude").GetDecimal());
        Assert.Equal(longitude, saved.GetProperty("defaultMapLongitude").GetDecimal());
        Assert.Equal(zoom, saved.GetProperty("defaultMapZoom").GetInt32());
        Assert.Equal(timeZone, saved.GetProperty("timeZone").GetString());
    }

    [Theory]
    [InlineData("theme", "neon")]
    [InlineData("defaultMapLatitude", 91)]
    [InlineData("defaultMapLongitude", -181)]
    [InlineData("defaultMapZoom", 21)]
    [InlineData("preferredTransportMode", "boat")]
    [InlineData("timeZone", "Mars/Olympus_Mons")]
    public async Task Put_InvalidValue_ReturnsFieldErrorAndKeepsPreviousSettings(string field, object value)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        await client.PutSettingsAsync(ValidPreferences());
        var rejected = Override(ValidPreferences(), field, value);

        using var response = await client.PutSettingsAsync(rejected);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        using var get = await client.GetAsync("/api/v1/settings");
        AssertPreferences(await get.Content.ReadFromJsonAsync<JsonElement>(), ValidPreferences());
    }

    [Fact]
    public async Task Put_MissingField_ReturnsFieldErrorAndSavesNothing()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutSettingsAsync(new
        {
            theme = "dark",
            defaultMapLatitude = 1,
            defaultMapLongitude = 1,
            defaultMapZoom = 3,
            preferredTransportMode = "car"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("timeZone", out _));
        using var get = await client.GetAsync("/api/v1/settings");
        AssertPreferences(await get.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("ownerSubject")]
    [InlineData("ownerIssuer")]
    [InlineData("subject")]
    public async Task Put_IdentityFieldInBody_IsRejectedAndSavesNothing(string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutAsJsonAsync("/api/v1/settings",
            Override(ValidPreferences(), field, "bob"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var get = await client.GetAsync("/api/v1/settings");
        AssertPreferences(await get.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
    }

    [Fact]
    public async Task Put_MalformedJson_ReturnsBadRequestAndSavesNothing()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PutAsync("/api/v1/settings",
            new StringContent("{ \"theme\": ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var get = await client.GetAsync("/api/v1/settings");
        AssertPreferences(await get.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
    }

    [Fact]
    public async Task Routes_DoNotExposeOtherAccountsByIdentifier()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        var otherAccount = Guid.CreateVersion7();

        using var read = await client.GetAsync($"/api/v1/settings/{otherAccount:D}");
        using var write = await client.PutAsJsonAsync($"/api/v1/settings/{otherAccount:D}", ValidPreferences());

        // no route matches; the fallback passes unmatched requests through, so routing answers 404.
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task Settings_AreIsolatedPerValidatedAccount_IgnoringClientIdentityHeaders()
    {
        using var factory = new ApiFactory();
        using var alice = factory.CreateClientFor("alice");
        using var bob = factory.CreateClientFor("bob");
        await alice.PutSettingsAsync(ValidPreferences());

        using var bobRead = await bob.GetAsync("/api/v1/settings");
        using var bobSpoofed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/settings");
        bobSpoofed.Headers.Add("X-User-Id", "alice");
        bobSpoofed.Headers.Authorization = bob.DefaultRequestHeaders.Authorization;
        using var spoofedResponse = await bob.SendAsync(bobSpoofed);
        using var aliceRead = await alice.GetAsync("/api/v1/settings");

        AssertPreferences(await bobRead.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
        AssertPreferences(await spoofedResponse.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
        AssertPreferences(await aliceRead.Content.ReadFromJsonAsync<JsonElement>(), ValidPreferences());
    }

    [Fact]
    public async Task BannedAccount_CanReadOwnSettingsButCannotReplaceThem()
    {
        using var factory = new ApiFactory();
        using var banned = factory.CreateClientFor("banned");

        using var write = await banned.PutAsJsonAsync("/api/v1/settings", ValidPreferences());
        using var read = await banned.GetAsync("/api/v1/settings");

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        AssertPreferences(await read.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
    }

    [Fact]
    public async Task DeactivatedAccount_IsForbiddenForReadAndWrite()
    {
        using var factory = new ApiFactory();
        using var deactivated = factory.CreateClientFor("deactivated");

        using var read = await deactivated.GetAsync("/api/v1/settings");
        using var write = await deactivated.PutAsJsonAsync("/api/v1/settings", ValidPreferences());

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task UsersUnavailable_FailsProtectedOperationsClosed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice");
        factory.Users!.Unavailable = true;

        using var read = await client.GetAsync("/api/v1/settings");
        using var write = await client.PutAsJsonAsync("/api/v1/settings", ValidPreferences());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, write.StatusCode);
        factory.Users.Unavailable = false;
        using var recovered = await client.GetAsync("/api/v1/settings");
        AssertPreferences(await recovered.Content.ReadFromJsonAsync<JsonElement>(), DefaultPreferences());
    }

    /**
     * the complete valid preference set shared by the contract tests.
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

    /**
     * the documented values returned before any save.
     * @return the expected default preference values.
     */
    private static object DefaultPreferences() => new
    {
        theme = "system",
        defaultMapLatitude = 0m,
        defaultMapLongitude = 0m,
        defaultMapZoom = 2,
        preferredTransportMode = "car",
        timeZone = "UTC"
    };

    /**
     * copies a preference object with one field replaced, for negative cases.
     * @param source the valid preference object.
     * @param field the camelCase JSON field to add or replace.
     * @param value the replacement value.
     * @return a new JSON-serializable object.
     */
    private static Dictionary<string, object?> Override(object source, string field, object? value)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(source))!;
        var copy = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        copy[field] = value;
        return copy;
    }

    /**
     * asserts that a settings JSON body contains exactly the expected values.
     * @param actual the parsed response body.
     * @param expected the expected preference object.
     */
    private static void AssertPreferences(JsonElement actual, object expected)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(expected))!;
        Assert.Equal(values.Count, actual.EnumerateObject().Count());
        foreach (var pair in values)
            Assert.Equal(pair.Value.ToString(), actual.GetProperty(pair.Key).ToString());
    }
}
