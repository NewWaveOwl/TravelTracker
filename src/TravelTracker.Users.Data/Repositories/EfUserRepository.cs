using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Data.Entities;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Repositories;

/**
 * Uses a PostgreSQL transaction and global advisory lock to consume bootstrap exactly once across hosts.
 * @param factory Creates isolated service-owned contexts.
 * @param timeProvider Supplies the UTC creation/audit time.
 */
public sealed class EfUserRepository(IDbContextFactory<UsersDbContext> factory, TimeProvider? timeProvider = null) : IUserRepository
{
    private static readonly EntityIdGenerator IdGenerator = new();

    /**
     * Reads the latest persisted account for an exact validated provider identity.
     * @param issuer Validated provider issuer.
     * @param subject Validated provider subject.
     * @param cancellationToken Request cancellation.
     * @return Detached account or null.
     */
    public async Task<UserAccount?> FindAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        var row = await database.Accounts.AsNoTracking().SingleOrDefaultAsync(entry => entry.Issuer == issuer && entry.Subject == subject, cancellationToken);
        return row is null ? null : Account(row);
    }

    /**
     * Locks first-admin initialization, commits its account, completion marker and audit together, and never re-elevates a replay.
     * @param issuer Validated approved issuer.
     * @param subject Validated approved subject.
     * @param cancellationToken Request cancellation.
     * @return First account or exact-identity replay, otherwise null.
     */
    public async Task<UserAccount?> BootstrapAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        var candidate = UserAccount.CreateAdministrator(new ProviderIdentity(issuer, subject), PersistedNow());
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EntityIdGenerator.ProvisioningLock})", cancellationToken);
        var consumed = await database.Bootstrap.AsNoTracking().SingleOrDefaultAsync(entry => entry.Id == 1, cancellationToken);
        if (consumed is not null)
        {
            if (consumed.Issuer != issuer || consumed.Subject != subject) return null;
            var previous = await database.Accounts.AsNoTracking().SingleOrDefaultAsync(entry => entry.PublicId == consumed.UserId && entry.Issuer == issuer && entry.Subject == subject, cancellationToken);
            return previous is null ? null : Account(previous);
        }
        if (await database.Accounts.AnyAsync(entry => entry.Role == "administrator", cancellationToken)) return null;
        var row = await database.Accounts.SingleOrDefaultAsync(entry => entry.Issuer == issuer && entry.Subject == subject, cancellationToken);
        if (row is not null && row.Status != "active") return null;
        if (row is null)
        {
            row = await NewRecordAsync(database, candidate, cancellationToken);
            database.Accounts.Add(row);
        }
        else
        {
            row.Role = "administrator";
            row.ConcurrencyToken = Guid.NewGuid();
        }
        database.Bootstrap.Add(new BootstrapRow { Id = 1, UserId = row.PublicId, Issuer = issuer, Subject = subject, CompletedAt = candidate.CreatedAt });
        database.AdministratorAudit.Add(new AdministratorAuditRow { AuditId = Guid.CreateVersion7(), UserId = row.PublicId, Reason = "Initial administrator setup", NewRole = "administrator", RecordedAt = candidate.CreatedAt });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Account(row);
    }

    /**
     * Rechecks active administrator status in the same locked read transaction as the account list.
     * @param issuer Validated actor issuer.
     * @param subject Validated actor subject.
     * @param cancellationToken Request cancellation.
     * @return Accounts ordered by username and public ID, or null for an unauthorized actor.
     */
    public async Task<IReadOnlyList<UserAccount>?> ListForAdministratorAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EntityIdGenerator.ProvisioningLock})", cancellationToken);
        if (!await database.Accounts.AnyAsync(entry => entry.Issuer == issuer && entry.Subject == subject && entry.Role == "administrator" && entry.Status == "active", cancellationToken)) return null;
        var rows = await database.Accounts.AsNoTracking().OrderBy(entry => entry.NormalizedUsername).ThenBy(entry => entry.PublicId).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows.Select(Account).ToArray();
    }

    /**
     * Serializes role decisions with bootstrap, reauthorizes the actor live, and saves role plus audit atomically.
     * @param actorIssuer Validated actor issuer.
     * @param actorSubject Validated actor subject.
     * @param targetId Public target identifier.
     * @param role Desired exact role.
     * @param expectedRole Expected saved role to detect stale edits.
     * @param reason Written reason for the transition.
     * @param cancellationToken Request cancellation.
     * @return Categorized result; unchanged and rejected requests create no audit.
     */
    public async Task<UserRoleChangeResult> ChangeRoleAsync(string actorIssuer, string actorSubject, Guid targetId, string role, string expectedRole, string reason, CancellationToken cancellationToken)
    {
        if (role is not ("traveller" or "administrator") || expectedRole is not ("traveller" or "administrator") ||
            targetId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return new("invalid", null);
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EntityIdGenerator.ProvisioningLock})", cancellationToken);
        var actor = await database.Accounts.AsNoTracking().SingleOrDefaultAsync(entry => entry.Issuer == actorIssuer && entry.Subject == actorSubject, cancellationToken);
        if (actor is null || actor.Role != "administrator" || actor.Status != "active") return new("forbidden", null);
        var target = await database.Accounts.SingleOrDefaultAsync(entry => entry.PublicId == targetId, cancellationToken);
        if (target is null) return new("notfound", null);
        if (target.Role != expectedRole) return new("conflict", null);
        if (target.Role == role) return new("unchanged", null);
        if (role == "administrator" && target.Status != "active") return new("invalid", null);
        if (target.Role == "administrator" && target.Status == "active" && role == "traveller" &&
            await database.Accounts.CountAsync(entry => entry.Role == "administrator" && entry.Status == "active", cancellationToken) <= 1)
            return new("last-administrator", null);
        var previous = target.Role;
        target.Role = role;
        target.ConcurrencyToken = Guid.NewGuid();
        database.AdministratorAudit.Add(new AdministratorAuditRow
        {
            AuditId = Guid.CreateVersion7(), UserId = target.PublicId, ActorUserId = actor.PublicId,
            PreviousRole = previous, NewRole = role, Action = "role-change", Reason = reason.Trim(), RecordedAt = PersistedNow(),
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new("conflict", null);
        }
        await transaction.CommitAsync(cancellationToken);
        return new("changed", Account(target));
    }

    /**
     * Reads the clock and trims it to the precision PostgreSQL stores, so confirmed writes equal later reads.
     * @return The current UTC time with microsecond precision.
     */
    private DateTimeOffset PersistedNow()
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime();
        // PostgreSQL timestamptz stores microseconds; confirmed writes must equal later reads.
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    /**
     * Converts a domain account into a new row with Data-owned identifiers and concurrency state.
     * @param database The context that will persist the record.
     * @param account The account values to store.
     * @param cancellationToken Cancels internal identifier allocation.
     * @return A new unsaved user record.
     */
    private async Task<UserRecord> NewRecordAsync(UsersDbContext database, UserAccount account, CancellationToken cancellationToken) => new()
    {
        Id = await IdGenerator.NextAsync(database, cancellationToken),
        PublicId = account.PublicId,
        Issuer = account.ProviderIdentity.Issuer,
        Subject = account.ProviderIdentity.Subject,
        Role = account.Role switch
        {
            UserRole.User => "traveller",
            UserRole.Administrator => "administrator",
            _ => throw new ArgumentOutOfRangeException(nameof(account), account.Role, "Unsupported user role."),
        },
        Status = account.Status switch
        {
            UserStatus.Active => "active",
            UserStatus.Banned => "banned",
            UserStatus.Deactivated => "deactivated",
            _ => throw new ArgumentOutOfRangeException(nameof(account), account.Status, "Unsupported user status."),
        },
        Username = account.Username,
        NormalizedUsername = account.Username?.ToUpperInvariant(),
        Address = account.Address,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt,
        ConcurrencyToken = Guid.NewGuid(),
    };

    /**
     * Restores the public domain account without exposing persistence-only values.
     * @param row The persisted account record.
     * @return The matching domain account.
     */
    private static UserAccount Account(UserRecord row) =>
        new(
            row.PublicId,
            new ProviderIdentity(row.Issuer, row.Subject),
            row.Role switch
            {
                "traveller" => UserRole.User,
                "administrator" => UserRole.Administrator,
                _ => throw new InvalidOperationException($"Unsupported persisted user role '{row.Role}'."),
            },
            row.Status switch
            {
                "active" => UserStatus.Active,
                "banned" => UserStatus.Banned,
                "deactivated" => UserStatus.Deactivated,
                _ => throw new InvalidOperationException($"Unsupported persisted user status '{row.Status}'."),
            },
            row.CreatedAt,
            row.Username,
            row.Address,
            row.UpdatedAt);
}
