using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Attractions.Data;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Proves creator-private/admin-shared authorization and PostgreSQL durability through HTTP.
 */
public sealed class PrivacyPersistenceTests
{
    [Fact]
    public async Task OnlyOwnerCanPublishPrivateAttractionAndSharedEditsRemainRestricted()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        using var created = await client.PostAsJsonAsync("/api/v1/attractions", Place());
        var location = created.Headers.Location!;
        var share = location + "/share";
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "bob"));
        using var hidden = await client.PostAsync(share, null);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        using var published = await client.PostAsync(share, null);
        Assert.Equal("shared", (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("visibility").GetString());
        using var edit = await client.PatchAsJsonAsync(location, new { name = "Change" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "bob"));
        using var repeated = await client.PostAsync(share, null);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=Private%20place"))!);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
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
    [Trait("Category", "PostgreSQL")]
    public async Task PostgreSqlConstraints_RejectInvalidValuesAndDuplicatePublicIdentifiers()
    {
        using var factory = new ApiFactory(usePostgres: true);
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

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task UpdatingADeletedRow_ThrowsConcurrencyInsteadOfRecreatingTheRecord()
    {
        using var factory = new ApiFactory(usePostgres: true);
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var repository = first.ServiceProvider.GetRequiredService<TravelTracker.Attractions.Data.Repositories.IAttractionRepository>();
        var other = second.ServiceProvider.GetRequiredService<TravelTracker.Attractions.Data.Repositories.IAttractionRepository>();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var stale = await repository.GetByIdAsync(id);
        Assert.NotNull(stale);
        Assert.Equal(1, await second.ServiceProvider.GetRequiredService<AttractionsDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"DELETE FROM \"Attractions\" WHERE \"PublicId\" = {id}"));
        stale.Name = "Stale replacement";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.UpdateAsync(stale));
        Assert.Null(await other.GetByIdAsync(id));
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
    [InlineData("role")]
    [InlineData("status")]
    public async Task ClientCannotSupplyOwnerIdentityOrAccountState(string field)
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
        await HttpTestSupport.AssertCreatedAsync(client, created);
        var location = created.Headers.Location!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "admin"));
        using var sharedCreate = await client.PostAsJsonAsync("/api/v1/attractions", Place("Shared place", "shared"));
        await HttpTestSupport.AssertCreatedAsync(client, sharedCreate);
        var sharedLocation = sharedCreate.Headers.Location!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(subject: "alice"));
        factory.Users!.ForcedStatus = "banned";
        using var readOwn = await client.GetAsync(location);
        using var readShared = await client.GetAsync(sharedLocation);
        using var create = await client.PostAsJsonAsync("/api/v1/attractions", Place("Forbidden"));
        using var patch = await client.PatchAsJsonAsync(location, new { name = "Forbidden" });
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions");
        Assert.Equal(HttpStatusCode.OK, readOwn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readShared.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Single(list!);
        factory.Users.ForcedStatus = "deactivated";
        using var denied = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task UnavailableUsers_FailsClosedWithoutPersistingWritesButHealthIsPublic()
    {
        using var factory = new ApiFactory { UsersUnavailable = true };
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<TravelTracker.Attractions.Data.Repositories.IAttractionRepository>();
        var visibleBefore = (await repository.GetVisibleAsync(ApiFactory.Issuer, "test-traveller", includeShared: true)).Count;
        using var create = await client.PostAsJsonAsync("/api/v1/attractions", Place());
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(visibleBefore, (await repository.GetVisibleAsync(ApiFactory.Issuer, "test-traveller", includeShared: true)).Count);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
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
            Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("_InitialAttractions", StringComparison.Ordinal));
            Assert.False(context.Database.HasPendingModelChanges());
            using var next = await client.PostAsJsonAsync("/api/v1/attractions", Place("After restart"));
            Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task MixedInvalidPatch_PersistsNoPartialChanges()
    {
        using var factory = new ApiFactory(usePostgres: true);
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
