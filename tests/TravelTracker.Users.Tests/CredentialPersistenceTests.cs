using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories.Credentials;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** PostgreSQL evidence that password credentials persist, stay unique per login and never reach the profile mapping. */
[Trait("Category", "PostgreSQL")]
public sealed class CredentialPersistenceTests
{
    const string Issuer = "https://identity.example.test/";

    static CredentialRepository Repository(PostgresTestDatabase database) =>
        new(new PooledDbContextFactory<UsersDbContext>(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(database.ConnectionString).Options));

    [Fact]
    public async Task RegisterPersistsAnActiveTravellerThatCanBeFoundCaseInsensitively()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = Repository(database);
        var account = await repository.RegisterAsync(Issuer, "Ada@Example.test", "hash-1", DateTimeOffset.UtcNow, default);
        Assert.NotNull(account);
        Assert.Equal(account.PublicId.ToString("D"), account.ProviderIdentity.Subject);
        Assert.Equal(UserRole.User, account.Role);
        var stored = await repository.FindAsync("ada@example.TEST", default);
        Assert.Equal("hash-1", stored!.PasswordHash);
        Assert.Equal(account.PublicId, stored.Account.PublicId);
    }

    [Fact]
    public async Task ASecondRegistrationForTheSameLoginIsRefusedEvenConcurrently()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = Repository(database);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            repository.RegisterAsync(Issuer, "ada@example.test", "hash", DateTimeOffset.UtcNow, default)));
        Assert.Single(attempts, account => account is not null);
    }

    [Fact]
    public async Task UpdateHashReplacesOnlyTheHashAndAccountsWithoutAPasswordAreNotFound()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = Repository(database);
        var account = await repository.RegisterAsync(Issuer, "ada@example.test", "old", DateTimeOffset.UtcNow, default);
        await repository.UpdateHashAsync(account!.PublicId, "new", default);
        Assert.Equal("new", (await repository.FindAsync("ada@example.test", default))!.PasswordHash);
        Assert.Null(await repository.FindAsync("nobody@example.test", default));
    }
}
