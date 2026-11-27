using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Entities;

namespace TravelTracker.Transport.Api.Tests;

/** Exercises real PostgreSQL migrations, constraints and independent-host restart durability. */
[Trait("Category", "PostgreSQL")]
public sealed class PersistenceTests
{
    [Fact]
    public async Task LegacyReadersSeeOnlySharedPointsAndRollbackRefusesToExposePrivateOnes()
    {
        using var store = new PostgresTestDatabase();
        await using var database = Context(store.ConnectionString);
        await TransportDatabase.InitializeAsync(database, true);
        var owner = Guid.NewGuid();
        database.RoutePoints.Add(new RoutePointRow { Id = 170, OwnerId = owner, PublicId = Guid.NewGuid(), Type = "transport_stop", Name = "Private" });
        await database.SaveChangesAsync();

        var count = await database.Database.SqlQuery<int>($"""SELECT count(*)::integer AS "Value" FROM "RoutePoints" """).SingleAsync();
        Assert.Equal(3, count);
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.Database.MigrateAsync("20261104184119_InitialTransport"));
        Assert.Contains("private route points", error.MessageText);
        Assert.Contains("20261126185741_PrivateRoutePoints", await database.Database.GetAppliedMigrationsAsync());
        Assert.True(await database.RoutePoints.AnyAsync(point => point.OwnerId == owner && !point.IsShared));
    }

    [Fact]
    public async Task OwnershipMigrationPreservesPreviouslySharedRoutePoints()
    {
        using var store = new PostgresTestDatabase();
        await using var database = Context(store.ConnectionString);
        await database.Database.MigrateAsync("20261104184119_InitialTransport");
        var legacyId = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RoutePoints" ("Id", "PublicId", "Type", "Name", "Address", "Latitude", "Longitude")
            VALUES (17, {legacyId}, 'transport_stop', 'Legacy station', '', 45, -73)
            """);
        await database.Database.MigrateAsync();
        var point = await database.RoutePoints.SingleAsync(row => row.PublicId == legacyId);
        Assert.True(point.IsShared);
        Assert.Null(point.OwnerId);

        await database.Database.MigrateAsync("20261104184119_InitialTransport");
        var restored = await database.Database.SqlQuery<Guid>($"""SELECT "PublicId" AS "Value" FROM "RoutePoints" """).SingleAsync();
        Assert.Equal(legacyId, restored);
    }

    [Fact]
    public async Task Restart_RetainsBikeAndPointIdentitiesAndRebuildsAllocator()
    {
        using var store = new PostgresTestDatabase();
        Guid modeId, pointId;
        int modeCountBeforeRestart, pointCountBeforeRestart;
        var sourceId = Guid.NewGuid();
        using (var factory = new ApiFactory { PostgresConnection = store.ConnectionString })
        using (var client = factory.CreateClient())
        {
            var mode = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bike", name = "Saved bicycle" });
            Assert.Equal(HttpStatusCode.Created, mode.StatusCode);
            modeId = (await mode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transportModeId").GetGuid();
            var point = await client.PostAsJsonAsync("/api/v1/route-points", new { type = "transport_stop", name = "Saved stop", lat = 45.5m, lon = -73.5m, sourceId });
            Assert.Equal(HttpStatusCode.Created, point.StatusCode);
            pointId = (await point.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("routePointId").GetGuid();
            modeCountBeforeRestart = (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/transport-modes"))!.Length;
            pointCountBeforeRestart = (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/route-points"))!.Length;
        }
        using (var factory = new ApiFactory { PostgresConnection = store.ConnectionString })
        using (var client = factory.CreateClient())
        {
            var mode = await client.GetFromJsonAsync<JsonElement>($"/api/v1/transport-modes/{modeId}");
            Assert.Equal("Saved bicycle", mode.GetProperty("name").GetString());
            var point = await client.GetFromJsonAsync<JsonElement>($"/api/v1/route-points/{pointId}");
            Assert.Equal(sourceId, point.GetProperty("sourceId").GetGuid());
            Assert.Equal(45.5m, point.GetProperty("lat").GetDecimal());

            var modesBeforeCreate = (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/transport-modes"))!;
            Assert.Equal(modeCountBeforeRestart, modesBeforeCreate.Length);
            var existingModeIds = modesBeforeCreate.Select(existing => existing.GetProperty("transportModeId").GetGuid()).ToHashSet();
            var created = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bike", name = "After restart" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var createdId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transportModeId").GetGuid();
            Assert.DoesNotContain(createdId, existingModeIds);
            Assert.Equal(modeCountBeforeRestart + 1, (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/transport-modes"))!.Length);
            Assert.Equal(pointCountBeforeRestart, (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/route-points"))!.Length);
        }
        await using var database = Context(store.ConnectionString);
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.Equal(modeCountBeforeRestart + 1, await database.TransportModes.CountAsync());
        Assert.Equal(pointCountBeforeRestart, await database.RoutePoints.CountAsync());
    }

    [Theory]
    [InlineData("transport-modes", "{\"code\":\"boat\",\"name\":\"Invalid\"}")]
    [InlineData("route-points", "{\"type\":\"airport\",\"name\":\"Invalid\",\"lat\":0,\"lon\":0}")]
    [InlineData("route-points", "{\"type\":\"transport_stop\",\"name\":\"Invalid\",\"lat\":91,\"lon\":0}")]
    public async Task InvalidInput_PersistsNothing(string resource, string json)
    {
        using var store = new PostgresTestDatabase();
        using var factory = new ApiFactory { PostgresConnection = store.ConnectionString, SeedExamples = false };
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/v1/" + resource, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var database = Context(store.ConnectionString);
        Assert.Equal(0, await database.TransportModes.CountAsync());
        Assert.Equal(0, await database.RoutePoints.CountAsync());
    }

    [Fact]
    public async Task Database_RejectsInvalidCoordinatesAndDuplicatePublicIdentity()
    {
        using var store = new PostgresTestDatabase();
        await using var database = Context(store.ConnectionString);
        await TransportDatabase.InitializeAsync(database, true);
        database.RoutePoints.Add(new RoutePointRow { Id = 100, IsShared = true, PublicId = Guid.NewGuid(), Type = "transport_stop", Name = "Invalid", Latitude = 91 });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        database.ChangeTracker.Clear();
        database.TransportModes.Add(new TransportModeRow { Id = 101, PublicId = Guid.Parse("00000000-0000-0000-0000-000000000104"), Code = "bike", DisplayName = "Duplicate" });
        error = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        database.ChangeTracker.Clear();
        Assert.Equal(3, await database.RoutePoints.CountAsync());
        Assert.Equal(6, await database.TransportModes.CountAsync());
    }

    /** Creates a PostgreSQL context for this test's disposable store.
     * @param connection Isolated database connection.
     * @return New service-owned context.
     */
    private static TransportDbContext Context(string connection) => new(new DbContextOptionsBuilder<TransportDbContext>().UseNpgsql(connection).Options);
}
