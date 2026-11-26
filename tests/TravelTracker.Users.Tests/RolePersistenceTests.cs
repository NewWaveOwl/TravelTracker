using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Data;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** PostgreSQL evidence for administrator authorization, role changes, audit and last-admin concurrency. */
[Trait("Category", "PostgreSQL")]
public sealed class RolePersistenceTests
{
    const string Issuer = "https://identity.example.test/";

    [Fact]
    public async Task AdministratorListsPersistedAccountsAndPromotionRecordsActorAndReason()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", default);
        var target = await Traveller(database, "traveller", "traveller");
        Assert.Null(await repository.ListForAdministratorAsync(Issuer, "traveller", default));
        var changed = await repository.ChangeRoleAsync(Issuer, "admin", target.PublicId, "administrator", "traveller", "  Approved account administrator  ", default);
        Assert.Equal("changed", changed.Code);
        var restarted = database.Repository();
        var listed = await restarted.ListForAdministratorAsync(Issuer, "admin", default);
        Assert.Equal(2, listed!.Count);
        Assert.Equal(UserRole.Administrator, listed.Single(account => account.PublicId == target.PublicId).Role);
        await using var check = database.Context();
        var audit = await check.Database.SqlQueryRaw<AuditEntry>("SELECT \"UserId\", \"ActorUserId\", \"PreviousRole\", \"NewRole\", \"Reason\", \"Action\" FROM \"AdministratorAudit\" WHERE \"Action\" = 'role-change'").SingleAsync();
        Assert.Equal(target.PublicId, audit.UserId);
        Assert.Equal(admin!.PublicId, audit.ActorUserId);
        Assert.Equal("traveller", audit.PreviousRole);
        Assert.Equal("administrator", audit.NewRole);
        Assert.Equal("Approved account administrator", audit.Reason);
    }

    [Fact]
    public async Task LastActiveAdministratorCannotDemoteItself()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", default);
        var result = await repository.ChangeRoleAsync(Issuer, "admin", admin!.PublicId, "traveller", "administrator", "Requested role removal", default);
        Assert.Equal("last-administrator", result.Code);
        Assert.Equal(UserRole.Administrator, (await repository.FindAsync(Issuer, "admin", default))!.Role);
        await using var check = database.Context();
        Assert.Equal(0, await RoleAuditCount(check));
    }

    [Fact]
    public async Task ConcurrentSelfDemotionsLeaveOneActiveAdministrator()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var first = await database.Repository().BootstrapAsync(Issuer, "first", default);
        var second = await Traveller(database, "second", "second");
        Assert.Equal("changed", (await database.Repository().ChangeRoleAsync(Issuer, "first", second.PublicId, "administrator", "traveller", "Appoint second administrator", default)).Code);
        // both demotions reach the provisioning lock before either takes it, so they really contend for the last-admin check.
        var barrier = new AdvisoryLockBarrier(2);
        var outcomes = await Task.WhenAll(
            database.Repository(interceptor: barrier).ChangeRoleAsync(Issuer, "first", first!.PublicId, "traveller", "administrator", "First administrator stepping down", default),
            database.Repository(interceptor: barrier).ChangeRoleAsync(Issuer, "second", second.PublicId, "traveller", "administrator", "Second administrator stepping down", default));
        Assert.True(barrier.Completed, "both demotions must reach the provisioning lock together.");
        Assert.Single(outcomes, result => result.Code == "changed");
        Assert.Single(outcomes, result => result.Code == "last-administrator");
        await using var check = database.Context();
        Assert.Equal(1, await check.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"UserAccounts\" WHERE \"Role\" = 'administrator' AND \"Status\" = 'active'").SingleAsync());
    }

    [Fact]
    public async Task DemotedActorImmediatelyLosesListingAndRoleChangeAuthority()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        var first = await repository.BootstrapAsync(Issuer, "first", default);
        var second = await Traveller(database, "second", "second");
        await repository.ChangeRoleAsync(Issuer, "first", second.PublicId, "administrator", "traveller", "Appoint another administrator", default);
        Assert.Equal("changed", (await repository.ChangeRoleAsync(Issuer, "first", first!.PublicId, "traveller", "administrator", "Hand over administration", default)).Code);
        Assert.Null(await repository.ListForAdministratorAsync(Issuer, "first", default));
        Assert.Equal("forbidden", (await repository.ChangeRoleAsync(Issuer, "first", first.PublicId, "administrator", "traveller", "Attempt to restore own role", default)).Code);
    }

    [Fact]
    public async Task StaleExpectedRoleOrSameRoleCreatesNoWriteOrAudit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        await repository.BootstrapAsync(Issuer, "admin", default);
        var traveller = await Traveller(database, "traveller", "traveller");
        Assert.Equal("conflict", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.PublicId, "administrator", "administrator", "Stale role form", default)).Code);
        Assert.Equal("unchanged", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.PublicId, "traveller", "traveller", "No change requested", default)).Code);
        Assert.Equal(UserRole.User, (await repository.FindAsync(Issuer, "traveller", default))!.Role);
        await using var check = database.Context();
        Assert.Equal(0, await RoleAuditCount(check));
    }

    [Fact]
    public async Task InactiveAccountsCannotBePromotedOrActAsAdministrators()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        var admin = await repository.BootstrapAsync(Issuer, "admin", default);
        var traveller = await Traveller(database, "traveller", "traveller");
        await using (var update = database.Context())
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Status\" = 'banned' WHERE \"UserId\" = {traveller.PublicId}");
        Assert.Equal("invalid", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.PublicId, "administrator", "traveller", "Cannot promote a banned account", default)).Code);
        await using (var update = database.Context())
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Status\" = 'deactivated' WHERE \"UserId\" = {admin!.PublicId}");
        Assert.Null(await repository.ListForAdministratorAsync(Issuer, "admin", default));
        Assert.Equal("forbidden", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.PublicId, "administrator", "traveller", "Inactive actor denied", default)).Code);
    }

    [Fact]
    public async Task RepeatedProvisioningDoesNotOverwriteRoleUsernameOrStanding()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var admin = await database.Repository().BootstrapAsync(Issuer, "admin", default);
        var existing = await database.Profiles().GetOrCreateAsync(new ProviderIdentity(Issuer, "admin"), DateTimeOffset.UtcNow, default);
        Assert.Equal(admin, existing);
        // both first sign-ins reach the provisioning lock before either takes it, so one insert and one replay really race.
        var barrier = new AdvisoryLockBarrier(2);
        var attempts = await Task.WhenAll(
            database.Profiles(barrier).GetOrCreateAsync(new ProviderIdentity(Issuer, "traveller"), DateTimeOffset.UtcNow, default),
            database.Profiles(barrier).GetOrCreateAsync(new ProviderIdentity(Issuer, "traveller"), DateTimeOffset.UtcNow, default));
        Assert.True(barrier.Completed, "both first sign-ins must reach the provisioning lock together.");
        Assert.Equal(attempts[0], attempts[1]);
        Assert.All(attempts, account => Assert.Equal(UserRole.User, account.Role));
        await using (var update = database.Context())
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Username\" = 'traveller', \"Status\" = 'banned' WHERE \"UserId\" = {attempts[0].PublicId}");
        var repeated = await database.Profiles().GetOrCreateAsync(new ProviderIdentity(Issuer, "traveller"), DateTimeOffset.UtcNow, default);
        Assert.Equal(UserStatus.Banned, repeated.Status);
        Assert.Equal(UserRole.User, repeated.Role);
        Assert.Equal("traveller", repeated.Username);
    }

    [Fact]
    public async Task AuditFailureRollsBackRoleMutation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        await repository.BootstrapAsync(Issuer, "admin", default);
        var traveller = await Traveller(database, "traveller", "traveller");
        await using (var update = database.Context())
            await update.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdministratorAudit\" ADD CONSTRAINT reject_test_roles CHECK (\"Action\" <> 'role-change')");
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.ChangeRoleAsync(Issuer, "admin", traveller.PublicId, "administrator", "traveller", "Rejected audit must roll back", default));
        Assert.Equal(UserRole.User, (await repository.FindAsync(Issuer, "traveller", default))!.Role);
        await using var check = database.Context();
        Assert.Equal(0, await RoleAuditCount(check));
    }

    [Theory]
    [InlineData("admin", "traveller", "Wrong role spelling")]
    [InlineData("administrator", "traveller", "   ")]
    public async Task InvalidTransitionsLeaveTheAccountUnchanged(string role, string expectedRole, string reason)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var repository = database.Repository();
        await repository.BootstrapAsync(Issuer, "admin", default);
        var traveller = await Traveller(database, "traveller", "traveller");
        Assert.Equal("invalid", (await repository.ChangeRoleAsync(Issuer, "admin", traveller.PublicId, role, expectedRole, reason, default)).Code);
        Assert.Equal(UserRole.User, (await repository.FindAsync(Issuer, "traveller", default))!.Role);
    }

    /**
     * Provisions a traveller through the self-profile path, then records its username.
     *
     * @param database The isolated test database.
     * @param subject The provider subject.
     * @param username The username to store.
     * @return The provisioned account.
     */
    static async Task<UserAccount> Traveller(PostgresTestDatabase database, string subject, string username)
    {
        var identity = new ProviderIdentity(Issuer, subject);
        await database.Profiles().GetOrCreateAsync(identity, DateTimeOffset.UtcNow, default);
        await using var update = database.Context();
        await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Username\" = {username}, \"NormalizedUsername\" = {username.ToUpperInvariant()} WHERE \"Subject\" = {subject} AND \"Issuer\" = {Issuer}");
        return (await database.Profiles().FindByProviderIdentityAsync(identity, default))!;
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
}
