using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Tests;

/** PostgreSQL evidence for administrator authorization, role changes, audit and last-admin concurrency. */
[Trait("Category", "PostgreSQL")]
public sealed class RolePersistenceTests
{
    const string Issuer = "https://identity.example.test/";
    const string AdminEmail = "first-admin@example.test";

    [Fact]
    public async Task AdministratorListsPersistedAccountsAndPromotionRecordsActorAndReason()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var target = await repository.GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default);
        Assert.Null(await repository.ListForAdministratorAsync(Issuer, "traveller", default));
        var changed = await repository.ChangeRoleAsync(Issuer, "admin", target.UserId, "administrator", "traveller", "  Approved account administrator  ", default);
        Assert.Equal("changed", changed.Code);
        var restarted = database.Repository();
        var listed = await restarted.ListForAdministratorAsync(Issuer, "admin", default);
        Assert.Equal(2, listed!.Count);
        Assert.Equal("administrator", listed.Single(x => x.UserId == target.UserId).Role);
        await using var check = database.Context();
        var audit = await check.Database.SqlQueryRaw<AuditEntry>("SELECT \"UserId\", \"ActorUserId\", \"PreviousRole\", \"NewRole\", \"Reason\", \"Action\" FROM \"AdministratorAudit\" WHERE \"Action\" = 'role-change'").SingleAsync();
        Assert.Equal(target.UserId, audit.UserId);
        Assert.Equal(admin!.UserId, audit.ActorUserId);
        Assert.Equal("traveller", audit.PreviousRole);
        Assert.Equal("administrator", audit.NewRole);
        Assert.Equal("Approved account administrator", audit.Reason);
    }

    [Fact]
    public async Task LastActiveAdministratorCannotDemoteItself()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var result = await repository.ChangeRoleAsync(Issuer, "admin", admin!.UserId, "traveller", "administrator", "Requested role removal", default);
        Assert.Equal("last-administrator", result.Code);
        Assert.Equal("administrator", (await repository.FindAsync(Issuer, "admin", default))!.Role);
        await using var check = database.Context();
        Assert.Equal(0, await RoleAuditCount(check));
    }

    [Fact]
    public async Task ConcurrentSelfDemotionsLeaveOneActiveAdministrator()
    {
        await using var database = await Database.CreateAsync();
        var first = await database.Repository().BootstrapAsync(Issuer, "first", AdminEmail, default);
        var second = await database.Repository().GetOrCreateTravellerAsync(Issuer, "second", "second@example.test", default);
        Assert.Equal("changed", (await database.Repository().ChangeRoleAsync(Issuer, "first", second.UserId, "administrator", "traveller", "Appoint second administrator", default)).Code);
        var outcomes = await Task.WhenAll(
            database.Repository().ChangeRoleAsync(Issuer, "first", first!.UserId, "traveller", "administrator", "First administrator stepping down", default),
            database.Repository().ChangeRoleAsync(Issuer, "second", second.UserId, "traveller", "administrator", "Second administrator stepping down", default));
        Assert.Single(outcomes, result => result.Code == "changed");
        Assert.Single(outcomes, result => result.Code == "last-administrator");
        await using var check = database.Context();
        Assert.Equal(1, await check.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"UserAccounts\" WHERE \"Role\" = 'administrator' AND \"Status\" = 'active'").SingleAsync());
    }

    [Fact]
    public async Task DemotedActorImmediatelyLosesListingAndRoleChangeAuthority()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var first = await repository.BootstrapAsync(Issuer, "first", AdminEmail, default);
        var second = await repository.GetOrCreateTravellerAsync(Issuer, "second", "second@example.test", default);
        await repository.ChangeRoleAsync(Issuer, "first", second.UserId, "administrator", "traveller", "Appoint another administrator", default);
        Assert.Equal("changed", (await repository.ChangeRoleAsync(Issuer, "first", first!.UserId, "traveller", "administrator", "Hand over administration", default)).Code);
        Assert.Null(await repository.ListForAdministratorAsync(Issuer, "first", default));
        Assert.Equal("forbidden", (await repository.ChangeRoleAsync(Issuer, "first", first.UserId, "administrator", "traveller", "Attempt to restore own role", default)).Code);
    }

    [Fact]
    public async Task StaleExpectedRoleOrSameRoleCreatesNoWriteOrAudit()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var traveller = await repository.GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default);
        Assert.Equal("conflict", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.UserId, "administrator", "administrator", "Stale role form", default)).Code);
        Assert.Equal("conflict", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.UserId, "traveller", "traveller", "No change requested", default)).Code);
        Assert.Equal("traveller", (await repository.FindAsync(Issuer, "traveller", default))!.Role);
        await using var check = database.Context();
        Assert.Equal(0, await RoleAuditCount(check));
    }

    [Fact]
    public async Task InactiveAccountsCannotBePromotedOrActAsAdministrators()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var traveller = await repository.GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default);
        await using (var update = database.Context())
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Status\" = 'banned' WHERE \"UserId\" = {traveller.UserId}");
        Assert.Equal("invalid", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.UserId, "administrator", "traveller", "Cannot promote a banned account", default)).Code);
        await using (var update = database.Context())
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Status\" = 'deactivated' WHERE \"UserId\" = {admin!.UserId}");
        Assert.Null(await repository.ListForAdministratorAsync(Issuer, "admin", default));
        Assert.Equal("forbidden", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.UserId, "administrator", "traveller", "Inactive actor denied", default)).Code);
    }

    [Fact]
    public async Task RepeatedProvisioningDoesNotOverwriteRoleEmailOrStanding()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var existing = await repository.GetOrCreateTravellerAsync(Issuer, "admin", "changed@example.test", default);
        Assert.Equal(admin, existing);
        var attempts = await Task.WhenAll(
            database.Repository().GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default),
            database.Repository().GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default));
        Assert.Equal(attempts[0], attempts[1]);
        Assert.All(attempts, account => Assert.Equal("traveller", account.Role));
        await using (var update = database.Context())
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Status\" = 'banned' WHERE \"UserId\" = {attempts[0].UserId}");
        Assert.Equal("banned", (await repository.GetOrCreateTravellerAsync(Issuer, "traveller", "new@example.test", default)).Status);
    }

    [Fact]
    public async Task AuditFailureRollsBackRoleMutation()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var traveller = await repository.GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default);
        await using (var update = database.Context())
            await update.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdministratorAudit\" ADD CONSTRAINT reject_test_roles CHECK (\"Action\" <> 'role-change')");
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.ChangeRoleAsync(Issuer, "admin", traveller.UserId, "administrator", "traveller", "Rejected audit must roll back", default));
        Assert.Equal("traveller", (await repository.FindAsync(Issuer, "traveller", default))!.Role);
        await using var check = database.Context();
        Assert.Equal(0, await RoleAuditCount(check));
    }

    [Theory]
    [InlineData("admin", "traveller", "Wrong role spelling")]
    [InlineData("administrator", "traveller", "   ")]
    public async Task InvalidTransitionsLeaveTheAccountUnchanged(string role, string expectedRole, string reason)
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        await repository.BootstrapAsync(Issuer, "admin", AdminEmail, default);
        var traveller = await repository.GetOrCreateTravellerAsync(Issuer, "traveller", "traveller@example.test", default);
        Assert.Equal("invalid", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.UserId, role, expectedRole, reason, default)).Code);
        Assert.Equal("traveller", (await repository.FindAsync(Issuer, "traveller", default))!.Role);
    }

    static Task<int> RoleAuditCount(UsersDbContext context) => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"AdministratorAudit\" WHERE \"Action\" = 'role-change'").SingleAsync();

    sealed class AuditEntry
    {
        public Guid UserId { get; set; }
        public Guid? ActorUserId { get; set; }
        public string PreviousRole { get; set; } = "";
        public string NewRole { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Action { get; set; } = "";
    }

    /** Owns one uniquely named disposable test database. */
    sealed class Database(string administrativeConnection, string name, string connection) : IAsyncDisposable
    {
        public UsersDbContext Context() => new(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(connection).Options);
        public EfUserRepository Repository() => new(new PooledDbContextFactory<UsersDbContext>(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(connection).Options));

        /**
         * Creates and migrates one isolated PostgreSQL database.
         * @return Database to dispose after the scenario.
         */
        public static async Task<Database> CreateAsync()
        {
            var administrativeConnection = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ?? throw new InvalidOperationException("Set TRAVELTRACKER_TEST_POSTGRES to an isolated PostgreSQL instance with CREATEDB permission.");
            var name = "traveltracker_roles_test_" + Guid.NewGuid().ToString("N");
            await using var administrative = new NpgsqlConnection(administrativeConnection);
            await administrative.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", administrative);
            await create.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(administrativeConnection) { Database = name, Pooling = false }.ConnectionString;
            var database = new Database(administrativeConnection, name, connection);
            try { await using var context = database.Context(); await context.Database.MigrateAsync(); return database; }
            catch { await database.DisposeAsync(); throw; }
        }

        /** Drops only this generated test database after its contexts are disposed. */
        public async ValueTask DisposeAsync()
        {
            await using var administrative = new NpgsqlConnection(administrativeConnection);
            await administrative.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", administrative);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
