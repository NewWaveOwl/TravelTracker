using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Attractions.Data;
using TravelTracker.Attractions.Api.Access;
using Microsoft.Extensions.Configuration;

namespace TravelTracker.Attractions.Api.Tests;

/**

 * Proves creator-private/admin-shared authorization and PostgreSQL durability through HTTP.

 */
public sealed class PrivacyPersistenceTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UsersProtocolErrors_FailClosedWithoutEscapingAsServerErrors(bool malformedCredential, bool invalidCharset)
    {
        using var handler = new UsersBackchannelHandler(false) { InvalidCharset = invalidCharset };
        using var http = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Users:AccessEndpoint"] = "https://users.example.test/resolve-access",
            ["Users:ServiceAccessToken"] = malformedCredential ? "invalid\ncredential" : "test-service-token"
        }).Build();
        var client = new UsersAccessClient(http, configuration);
        Assert.Null(await client.ResolveAsync(ApiFactory.Issuer, "alice", CancellationToken.None));
    }

    [Fact]
    public async Task EnablingExampleSeeding_DoesNotOverwriteOrCollideWithAnExistingUnseededDatabase()
    {
        using var database = new PostgresTestDatabase();
        Guid id;
        using (var factory = new ApiFactory(database.ConnectionString) { SeedExamples = false })
        using (var client = factory.CreateClient())
        {
            using var created = await client.PostAsJsonAsync("/api/v1/attractions", Place("Already persisted"));
            var record = await HttpTestSupport.AssertCreatedAsync(client, created);
            id = record.GetProperty("attractionId").GetGuid();
        }
        using (var factory = new ApiFactory(database.ConnectionString))
        using (var client = factory.CreateClient())
        {
            var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
            Assert.NotNull(list);
            Assert.Single(list!);
            Assert.Equal(id, list[0].GetProperty("attractionId").GetGuid());
            Assert.Equal("Already persisted", list[0].GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task PostgreSqlConstraints_RejectInvalidValuesAndDuplicatePublicIdentifiers()
    {
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AttractionsDbContext>();
        var invalid = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Database.ExecuteSqlRawAsync(
            "UPDATE \"Attractions\" SET \"Rating\" = 6 WHERE \"Id\" = 1"));
        Assert.Equal("23514", invalid.SqlState);
        var duplicate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Database.ExecuteSqlRawAsync(
            "UPDATE \"Attractions\" SET \"PublicId\" = '11111111-1111-1111-1111-111111111111' WHERE \"Id\" = 2"));
        Assert.Equal("23505", duplicate.SqlState);
        var repository = scope.ServiceProvider.GetRequiredService<TravelTracker.Attractions.Data.Repositories.IAttractionRepository>();
        Assert.Equal(4m, (await repository.GetByIdAsync(Guid.Parse("11111111-1111-1111-1111-111111111111")))!.Rating);
    }

    private static object Place(string name = "Private place", string visibility = "private") =>
        new { name, visibility, lat = 45.123456789012345678901234567m, lon = -73m, estimatedVisitMinutes = 45 };

    [Theory]
    [InlineData("bob")]
    [InlineData("admin")]
    public async Task PrivatePlace_IsAbsentToOtherTravellersAndAdministrators(string other)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        using var created = await client.PostAsJsonAsync("/api/v1/attractions", Place());
        var saved = await HttpTestSupport.AssertCreatedAsync(client, created);
        var id = saved.GetProperty("attractionId").GetGuid();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: other));
        using var detail = await client.GetAsync($"/api/v1/attractions/{id}");
        using var patch = await client.PatchAsJsonAsync($"/api/v1/attractions/{id}", new { name = "Stolen" });
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=Private");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Empty(list!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attractions/{id}");
        Assert.Equal("Private place", unchanged.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SharedPlace_RequiresAdministratorForCreationAndEditing()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        using var rejected = await client.PostAsJsonAsync("/api/v1/attractions", Place("Shared example", "shared"));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=Shared%20example"))!);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "admin"));
        using var created = await client.PostAsJsonAsync("/api/v1/attractions", Place("Shared example", "shared"));
        var saved = await HttpTestSupport.AssertCreatedAsync(client, created);
        var id = saved.GetProperty("attractionId").GetGuid();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "bob"));
        using var read = await client.GetAsync($"/api/v1/attractions/{id}");
        using var patch = await client.PatchAsJsonAsync($"/api/v1/attractions/{id}", new { name = "Not allowed" });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }

    [Theory]
    [InlineData("ownerSubject")]
    [InlineData("ownerIssuer")]
    [InlineData("id")]
    public async Task ClientCannotSupplyOwnerOrInternalKey(string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var body = new Dictionary<string, object> { ["name"] = "Spoofed", ["lat"] = 45, ["lon"] = 0,
            ["estimatedVisitMinutes"] = 30, [field] = "someone-else" };
        using var rejected = await client.PostAsJsonAsync("/api/v1/attractions", body);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=Spoofed"))!);
    }

    [Fact]
    public async Task LiveStandingChanges_ImmediatelyRestrictBannedAndDeactivatedAccounts()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        using var created = await client.PostAsJsonAsync("/api/v1/attractions", Place());
        var saved = await HttpTestSupport.AssertCreatedAsync(client, created);
        var location = created.Headers.Location!;
        factory.Users!.ForcedStatus = "banned";
        using var readOwn = await client.GetAsync(location);
        using var create = await client.PostAsJsonAsync("/api/v1/attractions", Place("Forbidden"));
        using var patch = await client.PatchAsJsonAsync(location, new { name = "Forbidden" });
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.OK, readOwn.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Single(list!);
        factory.Users.ForcedStatus = "deactivated";
        using var denied = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.True(factory.Users.Calls >= 7);
    }

    [Fact]
    public async Task UnavailableUsers_FailsClosedWithoutPersistingWritesButHealthIsPublic()
    {
        using var factory = new ApiFactory { UsersUnavailable = true };
        using var client = factory.CreateClient();
        using var create = await client.PostAsJsonAsync("/api/v1/attractions", Place());
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<TravelTracker.Attractions.Data.Repositories.IAttractionRepository>();
        Assert.Equal(3, (await repository.GetAllAsync()).Count);
    }

    [Fact]
    public async Task CreateAndPatch_SurviveHostRestartAndMigrationsWithoutReseedingChanges()
    {
        using var database = new PostgresTestDatabase();
        Guid id;
        using (var factory = new ApiFactory(database.ConnectionString))
        using (var client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
            using var response = await client.PostAsJsonAsync("/api/v1/attractions", Place());
            var record = await HttpTestSupport.AssertCreatedAsync(client, response);
            id = record.GetProperty("attractionId").GetGuid();
            using var updated = await client.PatchAsJsonAsync(response.Headers.Location!, new { name = "Retained name", rating = 4.75m });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        using (var factory = new ApiFactory(database.ConnectionString))
        using (var client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
            var record = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attractions/{id}");
            Assert.Equal("Retained name", record.GetProperty("name").GetString());
            Assert.Equal(4.75m, record.GetProperty("rating").GetDecimal());
            Assert.Equal(45.123456789012345678901234567m, record.GetProperty("lat").GetDecimal());
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AttractionsDbContext>();
            Assert.Single(await context.Database.GetAppliedMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
            using var next = await client.PostAsJsonAsync("/api/v1/attractions", Place("After restart"));
            Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        }
    }

    [Fact]
    public async Task MixedInvalidPatch_PersistsNoPartialChanges()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        const string url = "/api/v1/attractions/11111111-1111-1111-1111-111111111111";
        var before = await client.GetStringAsync(url);
        using var invalid = await client.PatchAsJsonAsync(url, new { name = "Would change", rating = 99m, address = "New address" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before, await client.GetStringAsync(url));
    }

    [Fact]
    public async Task Health_ReportsIndependentServiceAndTransportRoutesAreAbsent()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var health = await client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.Equal("Attractions", health.GetProperty("service").GetString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken());
        using var transport = await client.GetAsync("/api/v1/transport-modes/00000000-0000-0000-0000-000000000101");
        Assert.Equal(HttpStatusCode.NotFound, transport.StatusCode);
    }
}
