using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** PostgreSQL evidence that own-profile updates persist across restarts and respect standing atomically. */
[Trait("Category", "PostgreSQL")]
public sealed class ProfilePersistenceTests
{
    const string Issuer = "https://identity.example.test/";

    [Fact]
    public async Task UpdatedProfileSurvivesARepositoryRestartAndLeavesIdentityFieldsAlone()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var before = await database.Repository().GetOrCreateTravellerAsync(Issuer, "sub", "pat@example.test", default);
        Assert.Equal("pat", before.DisplayName);
        var result = await database.Repository().UpdateProfileAsync(Issuer, "sub", " Pat T ", "1 Main St", default);
        Assert.Equal("updated", result.Code);
        var after = (await database.Repository().FindAsync(Issuer, "sub", default))!;
        Assert.Equal("Pat T", after.DisplayName);
        Assert.Equal("1 Main St", after.Address);
        Assert.Equal(before with { DisplayName = "Pat T", Address = "1 Main St" }, after);
        Assert.Equal("updated", (await database.Repository().UpdateProfileAsync(Issuer, "sub", "Pat T", " ", default)).Code);
        Assert.Null((await database.Repository().FindAsync(Issuer, "sub", default))!.Address);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    public async Task RestrictedStandingRefusesTheUpdateAndKeepsTheStoredProfile(string status)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        await repository.GetOrCreateTravellerAsync(Issuer, "sub", "pat@example.test", default);
        await using (var context = database.Context())
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Status\" = {status}");
        Assert.Equal("forbidden", (await repository.UpdateProfileAsync(Issuer, "sub", "New", "New", default)).Code);
        var saved = (await repository.FindAsync(Issuer, "sub", default))!;
        Assert.Equal("pat", saved.DisplayName);
        Assert.Null(saved.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tab\there")]
    public async Task InvalidAndUnknownProfilesChangeNothing(string displayName)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        await repository.GetOrCreateTravellerAsync(Issuer, "sub", "pat@example.test", default);
        Assert.Equal("invalid", (await repository.UpdateProfileAsync(Issuer, "sub", displayName, null, default)).Code);
        Assert.Equal("notfound", (await repository.UpdateProfileAsync(Issuer, "nobody", "Pat", null, default)).Code);
        Assert.Equal("pat", (await repository.FindAsync(Issuer, "sub", default))!.DisplayName);
    }

    [Fact]
    public async Task UpdateOnlyEverTouchesTheCallersRow()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        await repository.GetOrCreateTravellerAsync(Issuer, "a", "a@example.test", default);
        await repository.GetOrCreateTravellerAsync(Issuer, "b", "b@example.test", default);
        await repository.UpdateProfileAsync(Issuer, "a", "Changed", "Somewhere", default);
        var other = (await repository.FindAsync(Issuer, "b", default))!;
        Assert.Equal("b", other.DisplayName);
        Assert.Null(other.Address);
    }
}
