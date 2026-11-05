using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Data.Entities;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Data.Repositories;

/**
 * Uses a PostgreSQL transaction and global advisory lock to consume bootstrap exactly once across hosts.
 * @param factory Creates isolated service-owned contexts.
 * @param timeProvider Supplies the UTC creation/audit time.
 */
public sealed class EfUserRepository(IDbContextFactory<UsersDbContext> factory, TimeProvider? timeProvider = null) : IUserRepository
{
    const long BootstrapLock = 0x5454555345520001;

    /**
     * Reads the latest persisted account for an exact validated provider identity.
     * @param issuer Validated provider issuer.
     * @param subject Validated provider subject.
     * @param cancellationToken Request cancellation.
     * @return Detached account or null.
     */
    public async Task<UserAccount?> FindAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);
        return row is null ? null : Account(row);
    }

    /**
     * Locks first-admin initialization, commits its account, completion marker and audit together, and never re-elevates a replay.
     * @param issuer Validated approved issuer.
     * @param subject Validated approved subject.
     * @param email Verified allowed email.
     * @param cancellationToken Request cancellation.
     * @return First account or exact-identity replay, otherwise null.
     */
    public async Task<UserAccount?> BootstrapAsync(string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        var candidate = UserAccount.CreateAdministrator(issuer, subject, email, PersistedNow());
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BootstrapLock})", cancellationToken);
        var consumed = await db.Bootstrap.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (consumed is not null)
        {
            if (consumed.Issuer != issuer || consumed.Subject != subject) return null;
            var previous = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == consumed.UserId && x.Issuer == issuer && x.Subject == subject, cancellationToken);
            return previous is null ? null : Account(previous);
        }
        if (await db.Accounts.AnyAsync(x => x.Role == "administrator", cancellationToken)) return null;
        var row = await db.Accounts.SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);
        if (row is not null && row.Status != "active") return null;
        if (row is null)
        {
            row = new UserRow { UserId = candidate.UserId, Issuer = candidate.Issuer, Subject = candidate.Subject, Email = candidate.Email,
                Role = candidate.Role, Status = candidate.Status, CreatedAt = candidate.CreatedAt, DisplayName = candidate.DisplayName };
            db.Accounts.Add(row);
        }
        else
        {
            row.Role = "administrator";
            row.Email = candidate.Email;
        }
        db.Bootstrap.Add(new BootstrapRow { Id = 1, UserId = row.UserId, Issuer = issuer, Subject = subject, CompletedAt = candidate.CreatedAt });
        db.AdministratorAudit.Add(new AdministratorAuditRow { AuditId = Guid.CreateVersion7(), UserId = row.UserId, Reason = "Initial administrator setup", NewRole = "administrator", RecordedAt = candidate.CreatedAt });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Account(row);
    }

    /**
     * Creates only an ordinary active traveller; a repeated login never changes saved role, email or standing.
     * @param issuer Validated issuer.
     * @param subject Validated subject.
     * @param email Verified provider email.
     * @param cancellationToken Request cancellation.
     * @return The existing or newly saved local account.
     */
    public async Task<UserAccount> GetOrCreateTravellerAsync(string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        var candidate = UserAccount.CreateTraveller(issuer, subject, email, PersistedNow());
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BootstrapLock})", cancellationToken);
        var saved = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);
        if (saved is not null) return Account(saved);
        var row = new UserRow { UserId = candidate.UserId, Issuer = candidate.Issuer, Subject = candidate.Subject, Email = candidate.Email,
            Role = candidate.Role, Status = candidate.Status, CreatedAt = candidate.CreatedAt, DisplayName = candidate.DisplayName };
        db.Accounts.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Account(row);
    }

    /**
     * Rechecks active administrator status in the same locked read transaction as the account list.
     * @param issuer Validated actor issuer.
     * @param subject Validated actor subject.
     * @param cancellationToken Request cancellation.
     * @return Accounts ordered by email and public ID, or null for an unauthorized actor.
     */
    public async Task<IReadOnlyList<UserAccount>?> ListForAdministratorAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BootstrapLock})", cancellationToken);
        if (!await db.Accounts.AnyAsync(x => x.Issuer == issuer && x.Subject == subject && x.Role == "administrator" && x.Status == "active", cancellationToken)) return null;
        var rows = await db.Accounts.AsNoTracking().OrderBy(x => x.Email).ThenBy(x => x.UserId).ToListAsync(cancellationToken);
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
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BootstrapLock})", cancellationToken);
        var actor = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Issuer == actorIssuer && x.Subject == actorSubject, cancellationToken);
        if (actor is null || actor.Role != "administrator" || actor.Status != "active") return new("forbidden", null);
        var target = await db.Accounts.SingleOrDefaultAsync(x => x.UserId == targetId, cancellationToken);
        if (target is null) return new("notfound", null);
        if (target.Role != expectedRole || target.Role == role) return new("conflict", null);
        if (role == "administrator" && target.Status != "active") return new("invalid", null);
        if (target.Role == "administrator" && target.Status == "active" && role == "traveller" &&
            await db.Accounts.CountAsync(x => x.Role == "administrator" && x.Status == "active", cancellationToken) <= 1)
            return new("last-administrator", null);
        var previous = target.Role;
        target.Role = role;
        db.AdministratorAudit.Add(new AdministratorAuditRow
        {
            AuditId = Guid.CreateVersion7(), UserId = target.UserId, ActorUserId = actor.UserId,
            PreviousRole = previous, NewRole = role, Action = "role-change", Reason = reason.Trim(), RecordedAt = PersistedNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new("changed", Account(target));
    }

    /**
     * Updates only the two profile columns in one conditional statement, so a status change cannot race the check.
     * @param issuer Validated actor issuer.
     * @param subject Validated actor subject.
     * @param displayName Requested display name, validated again here.
     * @param address Requested address, validated again here; blank clears it.
     * @param cancellationToken Request cancellation.
     * @return updated with the saved account, notfound, forbidden (banned or deactivated) or invalid; nothing changes unless updated.
     */
    public async Task<UserProfileUpdateResult> UpdateProfileAsync(string issuer, string subject, string? displayName, string? address, CancellationToken cancellationToken)
    {
        if (!UserAccount.TryNormalizeProfile(displayName, address, out var name, out var place, out _)) return new("invalid", null);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await db.Accounts.Where(x => x.Issuer == issuer && x.Subject == subject && x.Status == "active")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.DisplayName, name).SetProperty(x => x.Address, place), cancellationToken);
        var row = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);
        if (row is null) return new("notfound", null);
        return changed == 1 ? new("updated", Account(row)) : new("forbidden", null);
    }

    DateTimeOffset PersistedNow()
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime();
        // PostgreSQL timestamptz stores microseconds; confirmed writes must equal later reads.
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    static UserAccount Account(UserRow row) => new(row.UserId, row.Issuer, row.Subject, row.Email, row.Role, row.Status, row.CreatedAt, row.DisplayName, row.Address);
}
