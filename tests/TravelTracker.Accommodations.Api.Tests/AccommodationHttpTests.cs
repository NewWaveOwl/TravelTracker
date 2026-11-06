using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Accommodations.Api.Tests.Support;

namespace TravelTracker.Accommodations.Api.Tests;

/** Verifies accommodation route, DTO, validation, and visibility behavior over HTTP. */
public sealed class AccommodationHttpTests
{
    [Fact]
    public async Task Health_IsAnonymousAndDoesNotCallUsers()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var health = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal("Accommodations", health.GetProperty("service").GetString());
        Assert.Equal(0, factory.Users!.Calls);
    }

    [Fact]
    public async Task Create_Returns201LocationAndPublicDtoWithPrivateDefault()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.write catalogue.read");

        using var response = await client.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "  Harbour Inn  ",
            lat = 45.5m,
            lon = -73.5m
        });
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(new[] { "accommodationId", "name", "address", "lat", "lon", "visibility" },
            saved.EnumerateObject().Select(property => property.Name));
        Assert.NotEqual(Guid.Empty, saved.GetProperty("accommodationId").GetGuid());
        Assert.Equal("Harbour Inn", saved.GetProperty("name").GetString());
        Assert.Equal(string.Empty, saved.GetProperty("address").GetString());
        Assert.Equal(45.5m, saved.GetProperty("lat").GetDecimal());
        Assert.Equal(-73.5m, saved.GetProperty("lon").GetDecimal());
        Assert.Equal("private", saved.GetProperty("visibility").GetString());
        Assert.DoesNotContain("ownerIssuer", saved.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("ownerSubject", saved.EnumerateObject().Select(property => property.Name));

        using var details = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
    }

    [Theory]
    [InlineData("   ", 45, -73, "name")]
    [InlineData("Valid", 90.1, -73, "lat")]
    [InlineData("Valid", 45, -180.1, "lon")]
    public async Task Create_InvalidNameOrCoordinatesReturnsFieldErrorsAndPersistsNothing(
        string name, decimal latitude, decimal longitude, string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.write catalogue.read");

        using var response = await client.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name,
            lat = latitude,
            lon = longitude
        });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var collection = await client.GetAsync("/api/v1/accommodations");
        var records = await collection.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(field, problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(HttpStatusCode.OK, collection.StatusCode);
        Assert.Empty(records!);
    }

    [Fact]
    public async Task Create_ExplicitNullAddressReturnsFieldError()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.write");
        using var response = await client.PostAsync("/api/v1/accommodations",
            JsonContent.Create(new { name = "Inn", address = (object?)null, lat = 45m, lon = -73m }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("address", problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task Create_ClientCannotSupplyIdentityOwnershipRoleOrStatus()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.write catalogue.read");
        using var response = await client.PostAsync("/api/v1/accommodations", JsonContent.Create(new
        {
            accommodationId = Guid.CreateVersion7(),
            ownerIssuer = "https://spoofed.example/",
            ownerSubject = "admin",
            role = "administrator",
            status = "active",
            name = "Spoofed Place",
            lat = 45m,
            lon = -73m
        }));
        using var collection = await client.GetAsync("/api/v1/accommodations");
        var records = await collection.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(records!);
    }

    [Fact]
    public async Task Create_IdentityHeaderCannotChangeValidatedOwner()
    {
        using var factory = new ApiFactory();
        using var bob = factory.CreateClientFor("bob", "catalogue.write catalogue.read");
        bob.DefaultRequestHeaders.Add("X-User-Subject", "alice");
        using var response = await bob.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Bob's Private Inn",
            lat = 45m,
            lon = -73m
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("accommodationId").GetGuid();

        using var alice = factory.CreateClientFor("alice", "catalogue.read");
        using var aliceLookup = await alice.GetAsync($"/api/v1/accommodations/{id:D}");
        using var bobLookup = await bob.GetAsync($"/api/v1/accommodations/{id:D}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, aliceLookup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobLookup.StatusCode);
    }

    [Fact]
    public async Task PrivateRecord_IsAbsentFromAnotherUsersSearchAndDetail()
    {
        using var factory = new ApiFactory();
        using var alice = factory.CreateClientFor("alice", "catalogue.write catalogue.read");
        using var create = await alice.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Alice Private Inn",
            lat = 45m,
            lon = -73m
        });
        var saved = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = saved.GetProperty("accommodationId").GetGuid();
        using var bob = factory.CreateClientFor("bob", "catalogue.read");

        using var search = await bob.GetAsync("/api/v1/accommodations?name=Alice");
        using var detail = await bob.GetAsync($"/api/v1/accommodations/{id:D}");
        var results = await search.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Empty(results!);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
    }

    [Fact]
    public async Task SharedRecord_AdminCanCreateAndActiveReaderCanSearchAndView()
    {
        using var factory = new ApiFactory();
        using var admin = factory.CreateClientFor("admin", "catalogue.write");
        using var create = await admin.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Shared Harbour Hotel",
            visibility = "shared",
            lat = 45m,
            lon = -73m
        });
        var saved = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = saved.GetProperty("accommodationId").GetGuid();
        using var alice = factory.CreateClientFor("alice", "catalogue.read");

        using var search = await alice.GetAsync("/api/v1/accommodations?name=harbour");
        using var detail = await alice.GetAsync($"/api/v1/accommodations/{id:D}");
        var results = await search.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("shared", saved.GetProperty("visibility").GetString());
        Assert.Contains(results!, record => record.GetProperty("accommodationId").GetGuid() == id);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Traveller_CannotCreateSharedRecordAndNothingIsPersisted()
    {
        using var factory = new ApiFactory();
        using var alice = factory.CreateClientFor("alice", "catalogue.write catalogue.read");
        using var create = await alice.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Unauthorized Shared Inn",
            visibility = "shared",
            lat = 45m,
            lon = -73m
        });
        using var search = await alice.GetAsync("/api/v1/accommodations");
        var records = await search.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Empty(records!);
    }

    [Fact]
    public async Task BannedCallerReadsOnlyOwnPrivateRecord()
    {
        using var factory = new ApiFactory();
        var ownPrivateId = await AccommodationTestData.SeedAsync(factory, "Banned Own Place", "banned", shared: false);
        var anotherPrivateId = await AccommodationTestData.SeedAsync(factory, "Alice Private Place", "alice", shared: false);
        var sharedId = await AccommodationTestData.SeedAsync(factory, "Shared Place", "admin", shared: true);
        using var banned = factory.CreateClientFor("banned", "catalogue.read");

        using var collection = await banned.GetAsync("/api/v1/accommodations");
        var records = await collection.Content.ReadFromJsonAsync<JsonElement[]>();
        using var ownDetail = await banned.GetAsync($"/api/v1/accommodations/{ownPrivateId:D}");
        using var otherDetail = await banned.GetAsync($"/api/v1/accommodations/{anotherPrivateId:D}");
        using var sharedDetail = await banned.GetAsync($"/api/v1/accommodations/{sharedId:D}");

        Assert.Equal(HttpStatusCode.OK, collection.StatusCode);
        Assert.Equal(new[] { "Banned Own Place" }, records!.Select(record => record.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.OK, ownDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherDetail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sharedDetail.StatusCode);
    }

    [Fact]
    public async Task DeactivatedCallerIsForbiddenForReadsAndWrites()
    {
        using var factory = new ApiFactory();
        using var deactivated = factory.CreateClientFor("deactivated", "catalogue.read catalogue.write");
        using var read = await deactivated.GetAsync("/api/v1/accommodations");
        using var write = await deactivated.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Denied Place",
            lat = 45m,
            lon = -73m
        });

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task UsersUnavailable_FailsProtectedOperationClosed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.read");
        factory.Users!.Unavailable = true;

        using var response = await client.GetAsync("/api/v1/accommodations");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Search_UsesLiteralCaseInsensitiveNameAndDeterministicOrdering()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.write catalogue.read");
        foreach (var name in new[] { "Z Harbour Inn", "a Harbour Inn", "Harbour % Inn" })
        {
            using var create = await client.PostAsJsonAsync("/api/v1/accommodations", new { name, lat = 45m, lon = -73m });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        using var filtered = await client.GetAsync("/api/v1/accommodations?name=%25");
        using var allMatches = await client.GetAsync("/api/v1/accommodations?name=harbour");
        using var blank = await client.GetAsync("/api/v1/accommodations?name=%20");
        var percentResults = await filtered.Content.ReadFromJsonAsync<JsonElement[]>();
        var matches = await allMatches.Content.ReadFromJsonAsync<JsonElement[]>();
        var all = await blank.Content.ReadFromJsonAsync<JsonElement[]>();

        Assert.Single(percentResults!);
        Assert.Equal("Harbour % Inn", percentResults![0].GetProperty("name").GetString());
        Assert.Equal(new[] { "a Harbour Inn", "Harbour % Inn", "Z Harbour Inn" },
            matches!.Select(record => record.GetProperty("name").GetString()));
        Assert.Equal(3, all!.Length);
    }

    [Fact]
    public async Task Search_RepeatedNameParameterReturnsFieldError()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.read");

        using var response = await client.GetAsync("/api/v1/accommodations?name=inn&name=hotel");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    [Fact]
    public async Task GetById_MalformedGuidReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", "catalogue.read");

        using var response = await client.GetAsync("/api/v1/accommodations/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
