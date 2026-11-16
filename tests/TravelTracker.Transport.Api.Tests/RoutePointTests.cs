using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;

namespace TravelTracker.Transport.Api.Tests;

/**
 * Verifies route-point creation, lookup and input validation through HTTP.
 */
public sealed class RoutePointTests
{
    [Fact]
    public async Task ActiveTravellerCanReadRoutePoints()
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Role = "traveller";
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/route-points");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, factory.Users.Calls);
    }

    [Theory]
    [InlineData("banned", false)]
    [InlineData("deactivated", false)]
    [InlineData("active", true)]
    public async Task NonActiveOrUnknownAccountCannotReadRoutePoints(string status, bool unknownIdentity)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Status = status;
        factory.Users.UnknownIdentity = unknownIdentity;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/route-points");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("active", "administrator", false, HttpStatusCode.Created)]
    [InlineData("active", "traveller", false, HttpStatusCode.Forbidden)]
    [InlineData("banned", "administrator", false, HttpStatusCode.Forbidden)]
    [InlineData("deactivated", "administrator", false, HttpStatusCode.Forbidden)]
    [InlineData("active", "administrator", true, HttpStatusCode.Forbidden)]
    public async Task CreatePointRequiresActiveAdministratorFromUsersNotToken(string status, string liveRole,
        bool unknownIdentity, HttpStatusCode expectedStatus)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Status = status;
        factory.Users.Role = liveRole;
        factory.Users.UnknownIdentity = unknownIdentity;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            factory.CreateToken("catalogue.write", role: "administrator"));

        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "transport_stop", name = "Policy Test Stop", lat = 45m, lon = -73m,
                role = "administrator", status = "active", ownerSubject = "victim" });

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.Created)
        {
            using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(saved.RootElement.TryGetProperty("role", out _));
            Assert.False(saved.RootElement.TryGetProperty("status", out _));
            Assert.False(saved.RootElement.TryGetProperty("ownerSubject", out _));
            Assert.All(factory.Users.BearerTokens, token => Assert.Equal(ApiFactory.UsersServiceToken, token));
        }
        else
        {
            factory.Users.Status = "active";
            factory.Users.Role = "traveller";
            factory.Users.UnknownIdentity = false;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                factory.CreateToken("catalogue.read", role: "administrator"));
            var points = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/route-points");
            Assert.Empty(points!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatePointUsersFailureFailsClosedAndPersistsNothing(bool invalidResponse)
    {
        using var factory = new ApiFactory { SeedExamples = false };
        factory.Users.Unavailable = !invalidResponse;
        factory.Users.InvalidResponse = invalidResponse;
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "transport_stop", name = "Must Not Persist", lat = 45m, lon = -73m });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        factory.Users.Unavailable = false;
        factory.Users.InvalidResponse = false;
        var points = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/route-points");
        Assert.Empty(points!);
    }

    /**
     * Creates each supported route-point type and reads its Location.
     * @param type One of the three permitted point roles.
     * @return Completion after checking creation and lookup.
     */
    [Theory]
    [InlineData("accommodation")]
    [InlineData("attraction")]
    [InlineData("transport_stop")]
    public async Task CreatePoint_SupportedTypeReturns201AndSavedLocation(string type)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type, name = " Example Point ", lat = -90m, lon = 180m, sourceId });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("routePointId").GetGuid());
        Assert.Equal(type, saved.GetProperty("type").GetString());
        Assert.Equal("Example Point", saved.GetProperty("name").GetString());
        Assert.Equal(sourceId, saved.GetProperty("sourceId").GetGuid());
        Assert.Equal(-90m, saved.GetProperty("lat").GetDecimal());
        Assert.Equal(180m, saved.GetProperty("lon").GetDecimal());
    }

    /**
     * Reads a seeded route point.
     * @return Completion after checking the seed.
     */
    [Fact]
    public async Task GetPointById_KnownPointReturnsSeed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/route-points/00000000-0000-0000-0000-000000000201");
        Assert.Equal("accommodation", saved.GetProperty("type").GetString());
        Assert.Equal("Example Hotel", saved.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("sourceId").ValueKind);
    }

    /**
     * Returns 404 for an absent route point.
     * @return Completion after checking the lookup response.
     */
    [Fact]
    public async Task GetPointById_UnknownPointReturns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/route-points/00000000-0000-0000-0000-000000000999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /**
     * Rejects an unsupported point role.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreatePoint_UnsupportedTypeReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "restaurant", name = "Example", lat = 45m, lon = -73m });
        await HttpTestSupport.AssertValidationAsync(response);
    }

    /**
     * Rejects latitude outside the permitted coordinate range.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreatePoint_InvalidLatitudeReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "transport_stop", name = "Example", lat = 90.1m, lon = -73m });
        await HttpTestSupport.AssertValidationAsync(response);
    }
}
