using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories.Profile;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Tests;

[Trait("Category", "PostgreSQL")]
public sealed class UserRepositoryPostgreSqlTests
{
    static readonly DateTimeOffset CreatedAt = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migration_UpgradesAndRetainsAccountsAcrossRestart()
    {
        await using var database = new PostgresTestDatabase();
        {
            var factory = CreateFactory(database.ConnectionString);
            await using var context = await factory.CreateDbContextAsync();
            await context.GetService<IMigrator>().MigrateAsync("20261104171950_AdministratorRoleAudit");

            var publicId = Guid.CreateVersion7();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "UserAccounts" ("UserId", "Issuer", "Subject", "Email", "Role", "Status", "CreatedAt")
                VALUES ({publicId}, {"https://issuer.example/"}, {"legacy-subject"}, {"legacy@example.test"},
                        {"traveller"}, {"active"}, {CreatedAt})
                """);

            await context.Database.MigrateAsync();
        }

        var identity = new ProviderIdentity("https://issuer.example/", "legacy-subject");
        Guid savedPublicId;
        {
            var factory = CreateFactory(database.ConnectionString);
            var repository = new UserRepository(factory);
            var existing = await repository.FindByProviderIdentityAsync(identity, CancellationToken.None);
            Assert.NotNull(existing);
            savedPublicId = existing.PublicId;
            Assert.Equal("legacy@example.test", existing.VerifiedEmail);
            Assert.Null(existing.Email);

            var updated = await repository.UpdateProfileAsync(
                identity,
                "Legacy.User",
                "legacy@example.test",
                "1 Main Street",
                true,
                CreatedAt.AddHours(1),
                CancellationToken.None);
            Assert.NotNull(updated);
            Assert.Equal("Legacy.User", updated.Username);
        }

        var restartedFactory = CreateFactory(database.ConnectionString);
        var restartedRepository = new UserRepository(restartedFactory);
        var persisted = await restartedRepository.FindByPublicIdAsync(savedPublicId, CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal("Legacy.User", persisted.Username);
        Assert.Equal("legacy@example.test", persisted.Email);
        Assert.Equal("legacy@example.test", persisted.VerifiedEmail);
        Assert.Equal("1 Main Street", persisted.Address);
        Assert.Equal(CreatedAt.AddHours(1), persisted.UpdatedAt);

        await using var verify = await restartedFactory.CreateDbContextAsync();
        var recordType = verify.Model.FindEntityType("TravelTracker.Users.Data.Entities.UserRecord");
        Assert.NotNull(recordType);
        var token = recordType.FindProperty("ConcurrencyToken");
        Assert.NotNull(token);
        Assert.True(token.IsConcurrencyToken);
        Assert.False(token.IsNullable);
    }

    [Fact]
    public async Task UniqueIndexes_EnforcePublicIdentityAndNormalizedUsernameButAllowNullUsernames()
    {
        await using var database = new PostgresTestDatabase();
        var factory = CreateFactory(database.ConnectionString);
        await MigrateAsync(factory);
        var repository = new UserRepository(factory);
        var firstIdentity = new ProviderIdentity("https://issuer.example/", "first");
        var secondIdentity = new ProviderIdentity("https://issuer.example/", "second");
        var first = await repository.GetOrCreateAsync(firstIdentity, CreatedAt, CancellationToken.None);
        var second = await repository.GetOrCreateAsync(secondIdentity, CreatedAt, CancellationToken.None);
        var third = await repository.GetOrCreateAsync(
            new ProviderIdentity("https://issuer.example/", "third"),
            CreatedAt,
            CancellationToken.None);

        Assert.Null(first.Username);
        Assert.Null(first.Email);
        Assert.Null(third.Username);

        await repository.UpdateProfileAsync(firstIdentity, "Alice", null, null, false, CreatedAt.AddMinutes(1), CancellationToken.None);
        await Assert.ThrowsAsync<UserProfileUsernameConflictException>(() =>
            repository.UpdateProfileAsync(secondIdentity, "alice", null, null, false, CreatedAt.AddMinutes(2), CancellationToken.None));

        await using var context = await factory.CreateDbContextAsync();
        var indexes = context.Model.FindEntityType("TravelTracker.Users.Data.Entities.UserRecord")!.GetIndexes();
        var publicIdIndex = Assert.Single(indexes, index => index.Properties.Select(property => property.Name).SequenceEqual(["PublicId"]));
        var normalizedIndex = Assert.Single(indexes, index => index.Properties.Select(property => property.Name).SequenceEqual(["NormalizedUsername"]));
        var providerIdentityIndex = Assert.Single(indexes, index => index.Properties.Select(property => property.Name).SequenceEqual(["Issuer", "Subject"]));
        Assert.True(publicIdIndex.IsUnique);
        Assert.True(normalizedIndex.IsUnique);
        Assert.Equal("\"NormalizedUsername\" IS NOT NULL", normalizedIndex.GetFilter());
        Assert.True(providerIdentityIndex.IsUnique);

        var duplicateIdentity = await Assert.ThrowsAsync<PostgresException>(() => InsertAccountAsync(
            context,
            9_000_001,
            Guid.CreateVersion7(),
            firstIdentity.Issuer,
            firstIdentity.Subject));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicateIdentity.SqlState);

        var duplicatePublicId = await Assert.ThrowsAsync<PostgresException>(() => InsertAccountAsync(
            context,
            9_000_002,
            first.PublicId,
            "https://other-issuer.example/",
            "other-subject"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicatePublicId.SqlState);
    }

    [Fact]
    public async Task CheckConstraints_RejectInvalidIdentityEmailRoleAndStatus()
    {
        await using var database = new PostgresTestDatabase();
        var factory = CreateFactory(database.ConnectionString);
        await MigrateAsync(factory);
        await using var context = await factory.CreateDbContextAsync();

        await AssertCheckViolationAsync(InsertAccountAsync(context, 1, Guid.CreateVersion7(), "", "subject"));
        await AssertCheckViolationAsync(InsertAccountAsync(context, 2, Guid.CreateVersion7(), "https://issuer.example/", ""));
        await AssertCheckViolationAsync(InsertAccountAsync(context, 3, Guid.CreateVersion7(), "https://issuer.example/", "email", ""));
        await AssertCheckViolationAsync(InsertAccountAsync(context, 4, Guid.CreateVersion7(), "https://issuer.example/", "role", role: "unsupported"));
        await AssertCheckViolationAsync(InsertAccountAsync(context, 5, Guid.CreateVersion7(), "https://issuer.example/", "status", status: "unsupported"));
    }

    [Fact]
    public async Task ConcurrentFirstRequests_ReturnTheSamePersistedAccount()
    {
        await using var database = new PostgresTestDatabase();
        var factory = CreateFactory(database.ConnectionString);
        await MigrateAsync(factory);
        var identity = new ProviderIdentity("https://issuer.example/", "first-sign-in");

        var accounts = await Task.WhenAll(
            new UserRepository(factory).GetOrCreateAsync(identity, CreatedAt, CancellationToken.None),
            new UserRepository(factory).GetOrCreateAsync(identity, CreatedAt, CancellationToken.None));

        Assert.Equal(accounts[0].PublicId, accounts[1].PublicId);
        var persisted = await new UserRepository(factory).FindByProviderIdentityAsync(identity, CancellationToken.None);
        Assert.Equal(accounts[0], persisted);
    }

    [Fact]
    public async Task ConcurrentProfileUpdates_RejectTheStaleWrite()
    {
        await using var database = new PostgresTestDatabase();
        {
            var initialFactory = CreateFactory(database.ConnectionString);
            await MigrateAsync(initialFactory);
            var repository = new UserRepository(initialFactory);
            await repository.GetOrCreateAsync(
                new ProviderIdentity("https://issuer.example/", "concurrent"),
                CreatedAt,
                CancellationToken.None);
        }

        var barrier = new SaveChangesBarrier(2);
        var concurrentFactory = CreateFactory(database.ConnectionString, barrier);
        var concurrentRepository = new UserRepository(concurrentFactory);
        var account = await concurrentRepository.FindByProviderIdentityAsync(
            new ProviderIdentity("https://issuer.example/", "concurrent"),
            CancellationToken.None);
        Assert.NotNull(account);

        var attempts = await Task.WhenAll(
            CaptureConcurrencyAsync(() => concurrentRepository.UpdateProfileAsync(
                account.ProviderIdentity,
                null,
                "winner@example.test",
                null,
                false,
                CreatedAt.AddMinutes(1),
                CancellationToken.None)),
            CaptureConcurrencyAsync(() => concurrentRepository.UpdateProfileAsync(
                account.ProviderIdentity,
                null,
                null,
                "2 Main Street",
                true,
                CreatedAt.AddMinutes(1),
                CancellationToken.None)));

        Assert.IsType<DbUpdateConcurrencyException>(Assert.Single(attempts, attempt => attempt is not null));

        var verifyFactory = CreateFactory(database.ConnectionString);
        var saved = await new UserRepository(verifyFactory).FindByPublicIdAsync(account.PublicId, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.True(
            (saved.Email == "winner@example.test" && saved.Address is null) ||
            (saved.Email is null && saved.Address == "2 Main Street"));
    }

    static IDbContextFactory<UsersDbContext> CreateFactory(
        string connectionString,
        IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<UsersDbContext>()
            .UseNpgsql(connectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new TestDbContextFactory(options.Options);
    }

    static async Task MigrateAsync(IDbContextFactory<UsersDbContext> factory)
    {
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    static Task<int> InsertAccountAsync(
        UsersDbContext context,
        long id,
        Guid publicId,
        string issuer,
        string subject,
        string? email = null,
        string role = "traveller",
        string status = "active") =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "UserAccounts"
                ("Id", "UserId", "Issuer", "Subject", "Email", "Role", "Status", "CreatedAt",
                 "Username", "NormalizedUsername", "Address", "UpdatedAt", "ConcurrencyToken")
            VALUES
                ({id}, {publicId}, {issuer}, {subject}, {email}, {role}, {status}, {CreatedAt},
                 NULL, NULL, NULL, NULL, {Guid.CreateVersion7()})
            """);

    /**
     * Verifies a PostgreSQL check constraint rejects the supplied insert.
     * @param insert The account insert operation.
     * @return A completed task after confirming the check-violation state.
     */
    static async Task AssertCheckViolationAsync(Task<int> insert)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => insert);
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    static async Task<Exception?> CaptureConcurrencyAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return exception;
        }
    }

    sealed class SaveChangesBarrier(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == participants) release.TrySetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }

    sealed class TestDbContextFactory(DbContextOptions<UsersDbContext> options) : IDbContextFactory<UsersDbContext>
    {
        public UsersDbContext CreateDbContext() => new(options);

        public Task<UsersDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName = "traveltracker_users_test_" + Guid.NewGuid().ToString("N");

        public string ConnectionString { get; }

        public PostgresTestDatabase()
        {
            adminConnectionString = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
                throw new InvalidOperationException("PostgreSQL data tests require TRAVELTRACKER_TEST_POSTGRES with CREATEDB permission.");

            using var admin = new NpgsqlConnection(adminConnectionString);
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            create.ExecuteNonQuery();
            ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            }.ConnectionString;
        }

        public ValueTask DisposeAsync()
        {
            using var admin = new NpgsqlConnection(adminConnectionString);
            admin.Open();
            using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            drop.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }
}
