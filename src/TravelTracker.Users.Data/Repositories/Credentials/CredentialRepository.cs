using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Users.Data.Entities;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Repositories.Credentials;

/**
 * Persists password credentials on the Users-owned account table.
 * @param factory Creates contexts for the Users database.
 */
public sealed class CredentialRepository(IDbContextFactory<UsersDbContext> factory) : ICredentialRepository
{
    /** Allocates internal keys while the caller holds the provisioning lock. */
    private static readonly EntityIdGenerator IdGenerator = new();

    /** Folds a username the same way for every read and write. */
    private static string Normalize(string username) => username.Trim().ToUpperInvariant();

    /** @inheritdoc */
    public async Task<UserAccount?> RegisterAsync(string issuer, string username, string passwordHash, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({EntityIdGenerator.ProvisioningLock})", cancellationToken);
        var normalized = Normalize(username);
        if (await database.Accounts.AnyAsync(item => item.NormalizedUsername == normalized, cancellationToken)) return null;

        var publicId = Guid.CreateVersion7();
        var utc = createdAt.ToUniversalTime();
        var record = new UserRecord
        {
            Id = await IdGenerator.NextAsync(database, cancellationToken),
            PublicId = publicId,
            Issuer = issuer,
            // the subject is the public id, so a username change would never change who a token belongs to
            Subject = publicId.ToString("D"),
            Role = "traveller",
            Status = "active",
            Username = username.Trim(),
            NormalizedUsername = normalized,
            PasswordHash = passwordHash,
            // postgres keeps microseconds
            CreatedAt = new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero),
            ConcurrencyToken = Guid.NewGuid(),
        };
        database.Accounts.Add(record);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }
        await transaction.CommitAsync(cancellationToken);
        return ToAccount(record);
    }

    /** @inheritdoc */
    public async Task<StoredCredential?> FindAsync(string username, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var normalized = Normalize(username);
        var record = await database.Accounts.AsNoTracking().SingleOrDefaultAsync(
            item => item.NormalizedUsername == normalized && item.PasswordHash != null, cancellationToken);
        return record is null ? null : new StoredCredential(ToAccount(record), record.PasswordHash!);
    }

    /** @inheritdoc */
    public async Task UpdateHashAsync(Guid publicId, string passwordHash, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        await database.Accounts.Where(item => item.PublicId == publicId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.PasswordHash, passwordHash), cancellationToken);
    }

    /** Restores the domain account without exposing internal keys or the hash. */
    private static UserAccount ToAccount(UserRecord record) => new(
        record.PublicId,
        new ProviderIdentity(record.Issuer, record.Subject),
        record.Role == "administrator" ? UserRole.Administrator : UserRole.User,
        record.Status switch
        {
            "active" => UserStatus.Active,
            "banned" => UserStatus.Banned,
            "deactivated" => UserStatus.Deactivated,
            _ => throw new InvalidOperationException($"Unsupported persisted user status '{record.Status}'."),
        },
        record.CreatedAt, record.Username, record.Address, record.UpdatedAt);
}
