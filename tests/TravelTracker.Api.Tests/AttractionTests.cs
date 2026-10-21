using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Data;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Api.Services;
using TravelTracker.Domain.Models;

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

    [Fact]
    public async Task Create_OmittedAddressBecomesEmptyString()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "No Address Museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal(string.Empty, saved.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Create_NameIsTrimmed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "  Trimmed Name  ", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal("Trimmed Name", saved.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_UnknownPropertyReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/attractions", JsonContent.Create(new { name = "Museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 60, unknown = "x" }));
        await HttpTestSupport.AssertValidationAsync(response);
    }

    [Fact]
    public async Task Create_ExplicitNullAddressReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/attractions", JsonContent.Create(new { name = "Museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 60, address = (object?)null }));
        await HttpTestSupport.AssertValidationAsync(response);
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
        Assert.NotNull(list);
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
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("Old Port", results[0].GetProperty("name").GetString());
    }

    /**
     * Rejects ambiguous decoded name values without changing stored attractions.
     * @param query Repeated name keys, including encoded and case-equivalent keys.
     * @return Completion after checking field errors and unchanged catalogue contents.
     */
    [Theory]
    [InlineData("name=museum&name=port")]
    [InlineData("name=museum&name=museum")]
    [InlineData("name=&name=")]
    [InlineData("name&name=port")]
    [InlineData("name=%20&name=%20")]
    [InlineData("name=museum&%6Eame=port")]
    [InlineData("%6Eame=museum&na%6De=port")]
    [InlineData("name=museum&NAME=port")]
    public async Task GetAll_RepeatedName_ReturnsValidationWithoutChangingCatalogue(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetStringAsync("/api/v1/attractions");

        using var response = await client.GetAsync($"/api/v1/attractions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors").GetProperty("name");
        Assert.Contains("only once", Assert.Single(errors.EnumerateArray()).GetString());
        Assert.Equal(before, await client.GetStringAsync("/api/v1/attractions"));
    }

    /**
     * Keeps missing and blank single-value filters equivalent to the full catalogue.
     * @param query Missing, empty or whitespace-only name filter.
     * @return Completion after checking the entire ordered response.
     */
    [Theory]
    [InlineData("")]
    [InlineData("?name")]
    [InlineData("?name=")]
    [InlineData("?name=%20%20")]
    public async Task GetAll_MissingOrBlankName_ReturnsFullCatalogue(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var all = await client.GetStringAsync("/api/v1/attractions");

        using var response = await client.GetAsync($"/api/v1/attractions{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(all, await response.Content.ReadAsStringAsync());
    }

    /**
     * Keeps single filters trimmed, case-insensitive and decoded exactly once.
     * @param query Encoded single-value filter or unrelated query parameter.
     * @param expectedName Expected match, or null when the literal filter matches nothing.
     * @return Completion after checking successful search results.
     */
    [Theory]
    [InlineData("name=%20mUsEuM%20", "Montreal Museum")]
    [InlineData("%6Eame=port", "Old Port")]
    [InlineData("name=port&other=one&other=two", "Old Port")]
    [InlineData("name=%2520museum%2520", null)]
    [InlineData("name=museum%26name%3Dport", null)]
    [InlineData("name=%25", null)]
    [InlineData("name=_", null)]
    [InlineData("name=*", null)]
    [InlineData("name=.*", null)]
    public async Task GetAll_SingleName_PreservesLiteralSearch(string query, string? expectedName)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/attractions?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(results);
        if (expectedName is null) Assert.Empty(results);
        else Assert.Equal(expectedName, Assert.Single(results).GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetAll_DuplicateNames_DoesNotThrow_And_SortedByPublicId()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var r1 = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "Montreal Museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        var s1 = await HttpTestSupport.AssertCreatedAsync(client, r1);
        using var r2 = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "montreal museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        var s2 = await HttpTestSupport.AssertCreatedAsync(client, r2);

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
        Assert.NotNull(list);
        var matches = list.Where(e => string.Equals(e.GetProperty("name").GetString(), "montreal museum", StringComparison.OrdinalIgnoreCase))
                          .Select(e => e.GetProperty("attractionId").GetGuid())
                          .ToArray();
        Assert.True(matches.Length >= 2);
        var sorted = matches.OrderBy(g => g).ToArray();
        Assert.Equal(sorted, matches);
    }

    [Fact]
    public async Task Update_FullReplace_PersistsValuesAndKeepsIdentity()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IAttractionService>();
        var existing = await svc.GetByIdAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.NotNull(existing);
        var updated = new Attraction
        {
            Name = existing.Name + " Updated",
            Description = (existing.Description ?? string.Empty) + " Changed",
            Rating = existing.Rating ?? 3m,
            Address = existing.Address,
            Latitude = existing.Latitude,
            Longitude = existing.Longitude,
            EstimatedVisitMinutes = existing.EstimatedVisitMinutes
        };
        var returned = await svc.UpdateAsync(existing.AttractionId.PublicId, updated);
        Assert.NotNull(returned);
        Assert.Equal(existing.AttractionId.PublicId, returned.AttractionId.PublicId);
        var fetched = await svc.GetByIdAsync(existing.AttractionId.PublicId);
        Assert.NotNull(fetched);
        Assert.Equal(updated.Name, fetched.Name);
        Assert.Equal(updated.Description, fetched.Description);
        Assert.Equal(existing.AttractionId.PublicId, updated.AttractionId.PublicId);
    }

    [Fact]
    public async Task Patch_PartialUpdate_PreservesOmittedFields()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/attractions/11111111-1111-1111-1111-111111111111");
        var beforeDescription = before.GetProperty("description").ValueKind == JsonValueKind.Null ? null : before.GetProperty("description").GetString();
        var beforeRating = before.GetProperty("rating").ValueKind == JsonValueKind.Null ? (decimal?)null : before.GetProperty("rating").GetDecimal();

        using var patchResponse = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(new { name = "Montreal Museum Renamed" }));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Montreal Museum Renamed", updated.GetProperty("name").GetString());
        // omitted fields preserved
        Assert.Equal(beforeDescription, updated.GetProperty("description").ValueKind == JsonValueKind.Null ? null : updated.GetProperty("description").GetString());
        Assert.Equal(beforeRating, updated.GetProperty("rating").ValueKind == JsonValueKind.Null ? (decimal?)null : updated.GetProperty("rating").GetDecimal());
    }

    [Fact]
    public async Task Patch_ExplicitNull_ClearsDescriptionAndRating()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var patchResponse = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(new { description = (object?)null, rating = (object?)null }));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("rating").ValueKind);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("address")]
    [InlineData("lat")]
    [InlineData("lon")]
    [InlineData("estimatedVisitMinutes")]
    public async Task Patch_ExplicitNullOnRequiredFields_Returns400(string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        object body = field switch
        {
            "name" => new { name = (object?)null },
            "address" => new { address = (object?)null },
            "lat" => new { lat = (object?)null },
            "lon" => new { lon = (object?)null },
            "estimatedVisitMinutes" => new { estimatedVisitMinutes = (object?)null },
            _ => new { }
        };
        using var response = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(body));
        await HttpTestSupport.AssertValidationAsync(response);
    }

    [Fact]
    public async Task Patch_UnknownProperty_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(new { unknown = "x" }));
        await HttpTestSupport.AssertValidationAsync(response);
    }

    [Fact]
    public async Task Patch_UnknownValidId_Returns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsync("/api/v1/attractions/00000000-0000-0000-0000-000000000999", JsonContent.Create(new { name = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_MalformedId_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsync("/api/v1/attractions/not-a-guid", JsonContent.Create(new { name = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_ConcurrentUpdates_AllChangesApplied()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var id = "11111111-1111-1111-1111-111111111111";
        var patches = new object[]
        {
            new { name = "Concurrent Name" },
            new { description = "Concurrent desc" },
            new { rating = 3m },
            new { address = "Concurrent address" },
            new { lat = 46m },
            new { lon = -74m },
            new { estimatedVisitMinutes = 42 }
        };

        var tasks = patches.Select(p => client.PatchAsync($"/api/v1/attractions/{id}", JsonContent.Create(p))).ToArray();
        var responses = await Task.WhenAll(tasks);
        foreach (var r in responses) Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var updated = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attractions/{id}");
        Assert.Equal("Concurrent Name", updated.GetProperty("name").GetString());
        Assert.Equal("Concurrent desc", updated.GetProperty("description").GetString());
        Assert.Equal(3m, updated.GetProperty("rating").GetDecimal());
        Assert.Equal("Concurrent address", updated.GetProperty("address").GetString());
        Assert.Equal(46m, updated.GetProperty("lat").GetDecimal());
        Assert.Equal(-74m, updated.GetProperty("lon").GetDecimal());
        Assert.Equal(42, updated.GetProperty("estimatedVisitMinutes").GetInt32());
    }

    // Service tests moved from ServiceTests.cs
    [Fact]
    public async Task Service_GetAll_SearchTrimCaseInsensitive()
    {
        var repo = new TravelTracker.Data.Repositories.InMemoryAttractionRepository();
        var service = new TravelTracker.Api.Services.AttractionService(repo, EntityIdGenerator.ForSeedData());
        var results = await service.GetAllAsync(" museum ");
        Assert.All(results, r => Assert.Contains("museum", r.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Service_UpdatePartialAsync_Concurrent_AllChangesApplied()
    {
        var repo = new TravelTracker.Data.Repositories.InMemoryAttractionRepository();
        var service = new TravelTracker.Api.Services.AttractionService(repo, EntityIdGenerator.ForSeedData());
        var seeded = await service.CreateAsync(new TravelTracker.Domain.Models.Attraction
        {
            Name = "Seed",
            Address = "A",
            Latitude = 0m,
            Longitude = 0m,
            EstimatedVisitMinutes = 10,
            Description = "d",
            Rating = 1m
        });
        var id = seeded.AttractionId.PublicId;

        var patches = new[]
        {
            JsonDocument.Parse("{ \"name\": \"N1\" }").RootElement,
            JsonDocument.Parse("{ \"description\": \"D1\" }").RootElement,
            JsonDocument.Parse("{ \"rating\": 4 }").RootElement,
            JsonDocument.Parse("{ \"address\": \"Addr\" }").RootElement,
            JsonDocument.Parse("{ \"lat\": 12.34 }").RootElement,
            JsonDocument.Parse("{ \"lon\": -56.78 }").RootElement,
            JsonDocument.Parse("{ \"estimatedVisitMinutes\": 99 }").RootElement
        };

        var tasks = patches.Select(p => service.UpdatePartialAsync(id, p)).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.False(r.NotFound));
        Assert.All(results, r => Assert.Null(r.Errors));

        var final = await repo.GetByIdAsync(id);
        Assert.NotNull(final);
        Assert.Equal("N1", final!.Name);
        Assert.Equal("D1", final.Description);
        Assert.Equal(4m, final.Rating);
        Assert.Equal("Addr", final.Address);
        Assert.Equal(12.34m, final.Latitude);
        Assert.Equal(-56.78m, final.Longitude);
        Assert.Equal(99, final.EstimatedVisitMinutes);
    }
}
