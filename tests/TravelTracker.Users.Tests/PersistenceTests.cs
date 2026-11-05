using TravelTracker.Users.Domain.Models;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Data;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** PostgreSQL-only evidence for durable first-admin consumption, concurrent initialization and rollback. */
[Trait("Category", "PostgreSQL")]
public sealed class PersistenceTests
{
    const string Issuer = "https://identity.example.test/";
    const string Email = "administrator@example.test";

    [Fact]
    public async Task MigrationAndRestartPreserveTheFirstAdministratorAndAudit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var instant = DateTimeOffset.Parse("2026-11-04T12:34:56.1234567Z");
        var first = await database.Repository(new FixedClock(instant)).BootstrapAsync(Issuer, "account-one", Email, default);
        Assert.NotNull(first);
        Assert.Equal(0, first.CreatedAt.Ticks % 10);
        Assert.Equal(instant.Ticks - 7, first.CreatedAt.Ticks);
        var restarted = database.Repository();
        var read = await restarted.FindAsync(Issuer, "account-one", default);
        Assert.Equal(first, read);
        Assert.Equal(UserRole.Administrator, read!.Role);
        Assert.Equal(first, await restarted.BootstrapAsync(Issuer, "account-one", Email, default));
        Assert.Null(await restarted.BootstrapAsync(Issuer, "another-account", Email, default));
        await using var context = database.Context();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(1, await Count(context, "UserAccounts"));
        Assert.Equal(1, await Count(context, "AdministratorBootstrap"));
        Assert.Equal(1, await Count(context, "AdministratorAudit"));
    }

    [Fact]
    public async Task ConcurrentHostsPermitOnlyOneBootstrapIdentity()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var attempts = await Task.WhenAll(
            database.Repository().BootstrapAsync(Issuer, "account-one", Email, default),
            database.Repository().BootstrapAsync(Issuer, "account-two", Email, default));
        Assert.Single(attempts, account => account is not null);
        await using var context = database.Context();
        Assert.Equal(1, await Count(context, "UserAccounts"));
        Assert.Equal(1, await Count(context, "AdministratorBootstrap"));
        Assert.Equal(1, await Count(context, "AdministratorAudit"));
    }

    [Fact]
    public async Task DemotingTheOriginalAccountNeverReopensOrReElevatesBootstrap()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        var first = await repository.BootstrapAsync(Issuer, "account-one", Email, default);
        await using (var context = database.Context())
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Role\" = 'traveller' WHERE \"UserId\" = {first!.PublicId}");
        var replay = await repository.BootstrapAsync(Issuer, "account-one", Email, default);
        Assert.Equal(UserRole.User, replay!.Role);
        Assert.Equal(first!.PublicId, replay.PublicId);
        Assert.Null(await repository.BootstrapAsync(Issuer, "account-two", Email, default));
        await using var check = database.Context();
        Assert.Equal(1, await Count(check, "AdministratorAudit"));
    }

    [Fact]
    public async Task RemovingTheOriginalAccountDoesNotReopenBootstrap()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        var first = await repository.BootstrapAsync(Issuer, "account-one", Email, default);
        await using (var context = database.Context())
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"UserAccounts\" WHERE \"UserId\" = {first!.PublicId}");
        Assert.Null(await repository.BootstrapAsync(Issuer, "account-one", Email, default));
        Assert.Null(await repository.BootstrapAsync(Issuer, "account-two", Email, default));
        await using var check = database.Context();
        Assert.Equal(0, await Count(check, "UserAccounts"));
        Assert.Equal(1, await Count(check, "AdministratorBootstrap"));
        Assert.Equal(1, await Count(check, "AdministratorAudit"));
    }

    [Fact]
    public async Task FailedAuditWriteRollsBackTheAccountAndBootstrapMarkerTogether()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var context = database.Context())
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdministratorAudit\" ADD CONSTRAINT reject_test_audit CHECK (false)");
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Repository().BootstrapAsync(Issuer, "account-one", Email, default));
        await using var check = database.Context();
        Assert.Equal(0, await Count(check, "UserAccounts"));
        Assert.Equal(0, await Count(check, "AdministratorBootstrap"));
        Assert.Equal(0, await Count(check, "AdministratorAudit"));
        await check.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdministratorAudit\" DROP CONSTRAINT reject_test_audit");
        Assert.NotNull(await database.Repository().BootstrapAsync(Issuer, "account-one", Email, default));
    }

    static Task<int> Count(UsersDbContext context, string table) => (table switch
    {
        "UserAccounts" => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"UserAccounts\""),
        "AdministratorBootstrap" => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"AdministratorBootstrap\""),
        "AdministratorAudit" => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"AdministratorAudit\""),
        _ => throw new ArgumentException("Unknown test table.", nameof(table)),
    }).SingleAsync();

    sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
