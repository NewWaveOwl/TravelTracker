using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Api.Tests;

/**
 * Verifies attraction creation, lookup and input validation through HTTP.
 */
public sealed class AttractionTests
{
    /**
     * Reads the known seed and its public fields.
     * @return Completion after checking the saved representation.
     */
    [Fact]
    public async Task GetById_KnownAttractionReturnsSeed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/attractions/11111111-1111-1111-1111-111111111111");
        Assert.Equal("Montreal Museum", saved.GetProperty("name").GetString());
        Assert.Equal(45.4987m, saved.GetProperty("lat").GetDecimal());
        Assert.Equal(new[] { "attractionId", "name", "description", "rating", "address", "lat", "lon", "estimatedVisitMinutes" },
            saved.EnumerateObject().Select(field => field.Name));
    }

    /**
     * Returns 404 for an absent attraction.
     * @return Completion after checking the lookup response.
     */
    [Fact]
    public async Task GetById_UnknownAttractionReturns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions/00000000-0000-0000-0000-000000000999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /**
     * Creates a trimmed attraction and retrieves its generated identifier.
     * @return Completion after checking creation and lookup.
     */
    [Fact]
    public async Task Create_ValidAttractionReturns201AndSavedLocation()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = " New Museum ", description = "A new place.", rating = 5m, lat = 45m, lon = -73m, estimatedVisitMinutes = 60 });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("attractionId").GetGuid());
        Assert.Equal("New Museum", saved.GetProperty("name").GetString());
        Assert.Equal("A new place.", saved.GetProperty("description").GetString());
        Assert.Equal(5m, saved.GetProperty("rating").GetDecimal());
        Assert.Equal(string.Empty, saved.GetProperty("address").GetString());
        Assert.Equal(60, saved.GetProperty("estimatedVisitMinutes").GetInt32());
    }

    /**
     * Rejects a whitespace-only name.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task Create_BlankNameReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "   ", lat = 45m, lon = -73m, estimatedVisitMinutes = 60 });
        await HttpTestSupport.AssertValidationAsync(response);
    }

    /**
     * Rejects a rating outside the manual score range.
     * @param rating Score below zero or above five.
     * @return Completion after checking validation.
     */
    [Theory]
    [InlineData(-0.1)]
    [InlineData(5.1)]
    public async Task Create_InvalidRatingReturns400(double rating)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "Museum", rating, lat = 45m, lon = -73m, estimatedVisitMinutes = 60 });
        await HttpTestSupport.AssertValidationAsync(response);
    }

    /**
     * Lists all attractions deterministically ordered by name (OrdinalIgnoreCase).
     */
    [Fact]
    public async Task GetAll_ReturnsOrderedList()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
        var names = list.Select(e => e.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "Montreal Museum", "Old Port", "Science Centre" }, names);
    }

    /**
     * Searches by name substring (case-insensitive) using OrdinalIgnoreCase semantics.
     */
    [Fact]
    public async Task GetAll_SearchByName_ReturnsMatches()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var results = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=port");
        Assert.Single(results);
        Assert.Equal("Old Port", results[0].GetProperty("name").GetString());
    }
}
