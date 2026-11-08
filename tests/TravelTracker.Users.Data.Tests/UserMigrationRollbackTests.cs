using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TravelTracker.Users.Data.Repositories.Profile;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Tests;

/** Rolling the profile migrations back must never put an editable contact email into the provider-verified column. */
public sealed partial class UserRepositoryPostgreSqlTests
{
    const string LegacyMigration = "20261104171950_AdministratorRoleAudit";

    [Fact]
    public async Task Rollback_RestoresTheVerifiedEmailEvenAfterTheContactEmailWasEdited()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = await CreateFactory(database.ConnectionString).CreateDbContextAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(LegacyMigration);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "UserAccounts" ("UserId", "Issuer", "Subject", "Email", "Role", "Status", "CreatedAt")
            VALUES ({Guid.CreateVersion7()}, {"https://issuer.example/"}, {"edited"}, {"verified-edited@example.test"}, {"traveller"}, {"active"}, {CreatedAt}),
                   ({Guid.CreateVersion7()}, {"https://issuer.example/"}, {"untouched"}, {"verified-untouched@example.test"}, {"traveller"}, {"active"}, {CreatedAt})
            """);
        await migrator.MigrateAsync();

        // the owner edits their contact email after the upgrade
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "UserAccounts" SET "Email" = {"contact-edited@example.test"}, "UpdatedAt" = {CreatedAt} WHERE "Subject" = {"edited"}""");

        await migrator.MigrateAsync(LegacyMigration);

        var emails = await context.Database.SqlQuery<string>(
            $"""SELECT "Subject" || '=' || "Email" AS "Value" FROM "UserAccounts" ORDER BY "Subject" """).ToListAsync();
        Assert.Equal(["edited=verified-edited@example.test", "untouched=verified-untouched@example.test"], emails);
    }

    [Theory]
    [InlineData("contact-only@example.test")]
    [InlineData(null)]
    public async Task Rollback_RefusesWhenAnAccountHasNoVerifiedEmailAndLeavesTheDatabaseAtTheUpgradedSchema(string? contactEmail)
    {
        await using var database = new PostgresTestDatabase();
        var factory = CreateFactory(database.ConnectionString);
        await MigrateAsync(factory);
        var identity = new ProviderIdentity("https://issuer.example/", "no-verified-email");
        var repository = new UserRepository(factory);
        await repository.GetOrCreateAsync(identity, CreatedAt, CancellationToken.None);
        if (contactEmail is not null)
            await repository.UpdateProfileAsync(identity, null, contactEmail, null, false, CreatedAt.AddMinutes(1), CancellationToken.None);

        await using var context = await factory.CreateDbContextAsync();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync(LegacyMigration));
        Assert.Contains("no provider-verified email", failure.ToString());

        // the failed rollback must leave the account exactly as it was: still upgraded, contact email not promoted to verified
        var saved = await new UserRepository(factory).FindByProviderIdentityAsync(identity, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(contactEmail, saved.Email);
        Assert.Null(saved.VerifiedEmail);
    }
}
