using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Users.Data.Entities;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Repositories.Profile;

/**
 * Persists profile operations against the Users-owned PostgreSQL account table.
 * @param factory Creates contexts for the Users database.
 */
public sealed class UserRepository(IDbContextFactory<UsersDbContext> factory) : IUserRepository
{
    private static readonly EntityIdGenerator IdGenerator = new();

    /**
     * Finds an account by the exact validated issuer and subject pair.
     * @param identity The identity-provider issuer and subject.
     * @param cancellationToken Cancels the lookup.
     * @return The account or null when absent.
     */
    public async Task<UserAccount?> FindByProviderIdentityAsync(ProviderIdentity identity, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var record = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Issuer == identity.Issuer && item.Subject == identity.Subject,
            cancellationToken);
        return record is null ? null : ToAccount(record);
    }

    /**
     * Finds an account by its public UUID.
     * @param publicId The public account identifier.
     * @param cancellationToken Cancels the lookup.
     * @return The account or null when absent.
     */
    public async Task<UserAccount?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var record = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);
        return record is null ? null : ToAccount(record);
    }

    /**
     * Finds an existing profile or provisions an active traveller with empty optional profile fields.
     * @param identity The validated identity-provider issuer and subject.
     * @param createdAt The application time for a new account.
     * @param cancellationToken Cancels the lookup or save.
     * @return The existing or newly saved account.
     */
    public async Task<UserAccount> GetOrCreateAsync(ProviderIdentity identity, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Issuer == identity.Issuer && item.Subject == identity.Subject,
            cancellationToken);
        if (existing is not null) return ToAccount(existing);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({EntityIdGenerator.ProvisioningLock})", cancellationToken);
        existing = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Issuer == identity.Issuer && item.Subject == identity.Subject,
            cancellationToken);
        if (existing is not null) return ToAccount(existing);

        var account = UserAccount.CreateTraveller(identity, Utc(createdAt));
        var record = new UserRecord
        {
            Id = await IdGenerator.NextAsync(db, cancellationToken),
            PublicId = account.PublicId,
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            Role = RoleValue(account.Role),
            Status = StatusValue(account.Status),
            CreatedAt = account.CreatedAt,
            ConcurrencyToken = Guid.NewGuid(),
        };
        db.Accounts.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToAccount(record);
    }

    /**
     * Saves only supplied profile fields for an active owner and advances the concurrency token.
     * @param identity The validated owner identity.
     * @param username The replacement username, or null to preserve it.
     * @param email The replacement email, or null to preserve it.
     * @param address The replacement address, or null to clear it when supplied.
     * @param addressSpecified Whether the address field was supplied.
     * @param updatedAt The application time for this update.
     * @param cancellationToken Cancels the read or save.
     * @return Saved profile, unchanged inactive account, or null when absent.
     * @throws DbUpdateConcurrencyException when another writer changed the record after it was loaded.
     */
    public async Task<UserAccount?> UpdateProfileAsync(
        ProviderIdentity identity,
        string? username,
        string? email,
        string? address,
        bool addressSpecified,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var record = await db.Accounts.SingleOrDefaultAsync(
            item => item.Issuer == identity.Issuer && item.Subject == identity.Subject, cancellationToken);
        if (record is null) return null;
        if (record.Status != "active") return ToAccount(record);

        if (username is not null)
        {
            record.Username = username;
            record.NormalizedUsername = username.ToUpperInvariant();
        }
        if (email is not null) record.Email = email;
        if (addressSpecified) record.Address = address;
        record.UpdatedAt = Utc(updatedAt);
        record.ConcurrencyToken = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_UserAccounts_NormalizedUsername" })
        {
            throw new UserProfileUsernameConflictException();
        }
        return ToAccount(record);
    }

    /**
     * Restores the domain account without exposing internal keys or concurrency tokens.
     * @param record The persisted account row.
     * @return The matching domain account.
     */
    static UserAccount ToAccount(UserRecord record) =>
        new(
            record.PublicId,
            new ProviderIdentity(record.Issuer, record.Subject),
            ParseRole(record.Role),
            ParseStatus(record.Status),
            record.CreatedAt,
            record.Username,
            record.Email,
            record.Address,
            record.UpdatedAt) { VerifiedEmail = record.VerifiedEmail };

    /**
     * Converts a persisted role code to its domain value.
     * @param role The saved role code.
     * @return The domain role.
     */
    static UserRole ParseRole(string role) => role switch
    {
        "traveller" => UserRole.User,
        "administrator" => UserRole.Administrator,
        _ => throw new InvalidOperationException($"Unsupported persisted user role '{role}'."),
    };

    /**
     * Converts a persisted account status code to its domain value.
     * @param status The saved status code.
     * @return The domain account status.
     */
    static UserStatus ParseStatus(string status) => status switch
    {
        "active" => UserStatus.Active,
        "banned" => UserStatus.Banned,
        "deactivated" => UserStatus.Deactivated,
        _ => throw new InvalidOperationException($"Unsupported persisted user status '{status}'."),
    };

    /**
     * Converts a domain role to its stable persisted code.
     * @param role The domain role.
     * @return The persisted role code.
     */
    static string RoleValue(UserRole role) => role switch
    {
        UserRole.User => "traveller",
        UserRole.Administrator => "administrator",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported user role."),
    };

    /**
     * Converts a domain account status to its stable persisted code.
     * @param status The domain account status.
     * @return The persisted status code.
     */
    static string StatusValue(UserStatus status) => status switch
    {
        UserStatus.Active => "active",
        UserStatus.Banned => "banned",
        UserStatus.Deactivated => "deactivated",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported user status."),
    };

    /**
     * Converts a timestamp to PostgreSQL's UTC microsecond precision.
     * @param value The timestamp supplied by the owning application.
     * @return A UTC timestamp rounded down to microsecond precision.
     */
    static DateTimeOffset Utc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
}
