using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Attractions.Data;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Attractions.Api.Services;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Tests;

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
    public async Task Create_AddressIsTrimmed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "Address Museum", address = "  1 Main Street  ", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal("1 Main Street", saved.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Patch_AddressIsTrimmed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var patchResponse = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(new { address = "  2 Side Road  " }));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2 Side Road", updated.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Create_UnknownPropertyReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/attractions", JsonContent.Create(new { name = "Museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 60, unknown = "x" }));
        await HttpTestSupport.AssertValidationAsync(response, "$.unknown");
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
     * Accepts the inclusive rating boundaries.
     * @param rating Lowest or highest valid score.
     * @return Completion after checking creation.
     */
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Create_BoundaryRatingIsAccepted(double rating)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "Museum", rating, lat = 45m, lon = -73m, estimatedVisitMinutes = 60 });
        await HttpTestSupport.AssertCreatedAsync(client, response);
    }

    /**
     * Rejects values longer than the documented limits, naming the failing field.
     * @param field The oversized field.
     * @return Completion after checking validation.
     */
    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("address")]
    public async Task Create_OversizedTextReturns400(string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var body = new Dictionary<string, object> { ["name"] = "Museum", ["lat"] = 45m, ["lon"] = -73m, ["estimatedVisitMinutes"] = 60 };
        body[field] = new string('x', 5000);
        using var response = await client.PostAsJsonAsync("/api/v1/attractions", body);
        await HttpTestSupport.AssertValidationAsync(response, field);
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
        var names = list.Select(entry => entry.GetProperty("name").GetString()).ToArray();
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
     * @param query Encoded single-value filter that matches exactly one attraction.
     * @param expectedName Name of the single expected match.
     * @return Completion after checking successful search results.
     */
    [Theory]
    [InlineData("name=%20mUsEuM%20", "Montreal Museum")]
    [InlineData("%6Eame=port", "Old Port")]
    [InlineData("name=port&other=one&other=two", "Old Port")]
    public async Task GetAll_SingleName_PreservesLiteralSearch(string query, string expectedName)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/attractions?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(results);
        Assert.Equal(expectedName, Assert.Single(results).GetProperty("name").GetString());
    }

    /**
     * Treats encoded wildcards and separators in a single name filter as literal text.
     * @param query Encoded single-value filter that matches no attraction.
     * @return Completion after checking the empty search result.
     */
    [Theory]
    [InlineData("name=%2520museum%2520")]
    [InlineData("name=museum%26name%3Dport")]
    [InlineData("name=%25")]
    [InlineData("name=_")]
    [InlineData("name=*")]
    [InlineData("name=.*")]
    public async Task GetAll_SingleNameWithoutMatch_ReturnsEmptyLiteralSearch(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/attractions?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    /**
     * Breaks name ties by public identifier instead of failing.
     * @return Completion after checking the tie order.
     */
    [Fact]
    public async Task GetAll_DuplicateNames_AreSortedByPublicId()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var firstResponse = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "Montreal Museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        await HttpTestSupport.AssertCreatedAsync(client, firstResponse);
        using var secondResponse = await client.PostAsJsonAsync("/api/v1/attractions",
            new { name = "montreal museum", lat = 45m, lon = -73m, estimatedVisitMinutes = 30 });
        await HttpTestSupport.AssertCreatedAsync(client, secondResponse);

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
        Assert.NotNull(list);
        var matches = list.Where(entry => string.Equals(entry.GetProperty("name").GetString(), "montreal museum", StringComparison.OrdinalIgnoreCase))
                          .Select(entry => entry.GetProperty("attractionId").GetGuid())
                          .ToArray();
        // the seeded catalogue already holds one Montreal Museum
        Assert.Equal(3, matches.Length);
        Assert.Equal(matches.Order().ToArray(), matches);
    }

    [Fact]
    public async Task Patch_PartialUpdate_PreservesOmittedFields()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetFromJsonAsync<JsonElement>("/api/v1/attractions/11111111-1111-1111-1111-111111111111");
        var beforeDescription = OptionalString(before, "description");
        var beforeRating = OptionalDecimal(before, "rating");

        using var patchResponse = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(new { name = "Montreal Museum Renamed" }));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Montreal Museum Renamed", updated.GetProperty("name").GetString());
        // omitted fields preserved
        Assert.Equal(beforeDescription, OptionalString(updated, "description"));
        Assert.Equal(beforeRating, OptionalDecimal(updated, "rating"));
    }

    /** Reads a nullable string property, so a JSON null comes back as a C# null. */
    static string? OptionalString(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetString();

    /** Reads a nullable decimal property, so a JSON null comes back as a C# null. */
    static decimal? OptionalDecimal(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetDecimal();

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
        var body = new Dictionary<string, object?> { [field] = null };
        using var response = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(body));
        await HttpTestSupport.AssertValidationAsync(response, field);
    }

    [Fact]
    public async Task Patch_UnknownProperty_Returns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111", JsonContent.Create(new { unknown = "x" }));
        await HttpTestSupport.AssertValidationAsync(response, "unknown");
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
    [Trait("Category", "PostgreSQL")]
    public async Task Patch_ConcurrentUpdates_AllChangesApplied()
    {
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
        // every PATCH is held at its row lock until all seven have reached it, so the writers really overlap.
        var rendezvous = new WriteRendezvous(patches.Length);
        using var factory = new ApiFactory(usePostgres: true) { WriteInterceptor = new RendezvousCommandInterceptor(rendezvous, "FOR UPDATE") };
        using var client = factory.CreateClient();
        var id = "11111111-1111-1111-1111-111111111111";

        var tasks = patches.Select(patch => client.PatchAsync($"/api/v1/attractions/{id}", JsonContent.Create(patch))).ToArray();
        var responses = await Task.WhenAll(tasks);
        Assert.True(rendezvous.Completed, "all seven writers must reach the row lock together.");
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }

        var updated = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attractions/{id}");
        Assert.Equal("Concurrent Name", updated.GetProperty("name").GetString());
        Assert.Equal("Concurrent desc", updated.GetProperty("description").GetString());
        Assert.Equal(3m, updated.GetProperty("rating").GetDecimal());
        Assert.Equal("Concurrent address", updated.GetProperty("address").GetString());
        Assert.Equal(46m, updated.GetProperty("lat").GetDecimal());
        Assert.Equal(-74m, updated.GetProperty("lon").GetDecimal());
        Assert.Equal(42, updated.GetProperty("estimatedVisitMinutes").GetInt32());
    }

    [Fact]
    public async Task Service_GetAll_SearchTrimCaseInsensitive()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        factory.PrepareScope(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IAttractionService>();
        var results = await service.GetAllAsync(" museum ");
        Assert.NotEmpty(results);
        Assert.All(results, attraction => Assert.Contains("museum", attraction.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Service_UpdatePartialAsync_Concurrent_AllChangesApplied()
    {
        // every service call is held at its row lock until all seven have reached it, so the writers really overlap.
        var rendezvous = new WriteRendezvous(7);
        using var factory = new ApiFactory(usePostgres: true) { WriteInterceptor = new RendezvousCommandInterceptor(rendezvous, "FOR UPDATE") };
        using var scope = factory.Services.CreateScope();
        factory.PrepareScope(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IAttractionService>();
        var repository = scope.ServiceProvider.GetRequiredService<TravelTracker.Attractions.Data.Repositories.IAttractionRepository>();
        var seeded = await service.CreateAsync(new TravelTracker.Attractions.Domain.Models.Attraction
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

        var documents = new[]
        {
            "{ \"name\": \"N1\" }", "{ \"description\": \"D1\" }", "{ \"rating\": 4 }", "{ \"address\": \"Addr\" }",
            "{ \"lat\": 12.34 }", "{ \"lon\": -56.78 }", "{ \"estimatedVisitMinutes\": 99 }"
        }.Select(text => JsonDocument.Parse(text)).ToArray();
        var patches = documents.Select(document => document.RootElement).ToArray();

        var tasks = patches.Select(async patch =>
        {
            using var patchScope = factory.Services.CreateScope();
            factory.PrepareScope(patchScope.ServiceProvider);
            return await patchScope.ServiceProvider.GetRequiredService<IAttractionService>().UpdatePartialAsync(id, patch);
        }).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.True(rendezvous.Completed, "all seven writers must reach the row lock together.");
        Assert.All(results, result => Assert.False(result.NotFound));
        Assert.All(results, result => Assert.Null(result.Errors));

        var final = await repository.GetByIdAsync(id);
        Assert.NotNull(final);
        Assert.Equal("N1", final!.Name);
        Assert.Equal("D1", final.Description);
        Assert.Equal(4m, final.Rating);
        Assert.Equal("Addr", final.Address);
        Assert.Equal(12.34m, final.Latitude);
        Assert.Equal(-56.78m, final.Longitude);
        Assert.Equal(99, final.EstimatedVisitMinutes);
        foreach (var document in documents) document.Dispose();
    }
}
