using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TravelTracker.Users.Data.Repositories.Profile;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Tests;

/** Proves a profile save cannot land on an account whose standing changed after the account was loaded. */
public sealed partial class UserRepositoryPostgreSqlTests
{
    [Fact]
    public async Task ProfileUpdate_RejectsAWriteWhenStandingChangesBetweenLoadAndSave()
    {
        await using var database = new PostgresTestDatabase();
        var identity = new ProviderIdentity("https://issuer.example/", "banned-mid-save");
        await MigrateAsync(CreateFactory(database.ConnectionString));
        await new UserRepository(CreateFactory(database.ConnectionString)).GetOrCreateAsync(identity, CreatedAt, CancellationToken.None);

        // a writer that changes standing without advancing ConcurrencyToken, the case the token alone would miss
        var repository = new UserRepository(CreateFactory(database.ConnectionString, new BanBeforeSave(database.ConnectionString)));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.UpdateProfileAsync(
            identity, "Should.Not.Save", null, "1 Main Street", true, CreatedAt.AddMinutes(1), CancellationToken.None));

        var saved = await new UserRepository(CreateFactory(database.ConnectionString)).FindByProviderIdentityAsync(identity, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(UserStatus.Banned, saved.Status);
        Assert.Null(saved.Username);
        Assert.Null(saved.Address);
        Assert.Null(saved.UpdatedAt);
    }

    [Fact]
    public async Task ProfileUpdate_StillSavesWhenStandingIsUnchanged()
    {
        await using var database = new PostgresTestDatabase();
        var identity = new ProviderIdentity("https://issuer.example/", "stays-active");
        await MigrateAsync(CreateFactory(database.ConnectionString));
        var repository = new UserRepository(CreateFactory(database.ConnectionString));
        await repository.GetOrCreateAsync(identity, CreatedAt, CancellationToken.None);

        var updated = await repository.UpdateProfileAsync(identity, "Still.Active", null, null, false, CreatedAt.AddMinutes(1), CancellationToken.None);

        Assert.Equal("Still.Active", updated?.Username);
    }

    /**
     * Bans every account on a separate connection just before the profile save, without touching ConcurrencyToken.
     * @param connectionString The test database to update.
     */
    sealed class BanBeforeSave(string connectionString) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var ban = new NpgsqlCommand("""UPDATE "UserAccounts" SET "Status" = 'banned'""", connection);
            await ban.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }
}
