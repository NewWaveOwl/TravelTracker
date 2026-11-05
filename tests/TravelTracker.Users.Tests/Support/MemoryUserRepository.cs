using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Tests.Support;

/** Test account fixture with scripted administration outcomes; real policy and persistence are verified against PostgreSQL. */
public sealed class MemoryUserRepository : IUserRepository
{
    readonly Dictionary<Guid, UserAccount> accounts = [];
    Guid? bootstrapAccount;
    public UserAccount? Account => bootstrapAccount is { } id ? accounts.GetValueOrDefault(id) : accounts.Values.FirstOrDefault();
    public int BootstrapCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public IReadOnlyList<UserAccount>? AdministratorListResult { get; set; }
    public UserRoleChangeResult RoleChangeResult { get; set; } = new("forbidden", null);
    public (string Issuer, string Subject)? AdministratorListRequest { get; private set; }
    public (string Issuer, string Subject, Guid TargetId, string Role, string ExpectedRole, string Reason)? RoleChangeRequest { get; private set; }
    public int RoleChangeCalls { get; private set; }
    public int ProfileUpdateCalls { get; private set; }
    public (string Issuer, string Subject)? ProfileUpdateActor { get; private set; }

    /** Changes only the test account's standing to exercise the API's live read boundary. */
    public void Deactivate() => SetStatus(Account!.UserId, "deactivated");

    public void SetStatus(Guid id, string status) => accounts[id] = accounts[id] with { Status = status };

    public Task<UserAccount?> FindAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        ReadCalls++;
        return Task.FromResult(accounts.Values.SingleOrDefault(a => a.Issuer == issuer && a.Subject == subject));
    }

    public async Task<UserAccount> GetOrCreateTravellerAsync(string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(issuer, subject, cancellationToken);
        if (existing is not null) return existing;
        var account = UserAccount.CreateTraveller(issuer, subject, email, DateTimeOffset.Parse("2026-11-04T12:00:00Z"));
        accounts[account.UserId] = account;
        return account;
    }

    public async Task<UserAccount?> BootstrapAsync(string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        BootstrapCalls++;
        var existing = await FindAsync(issuer, subject, cancellationToken);
        if (bootstrapAccount is { } id) return existing?.UserId == id ? existing : null;
        if (accounts.Values.Any(a => a.Role == "administrator") || existing?.Status is "banned" or "deactivated") return null;
        var account = existing is null ? UserAccount.CreateAdministrator(issuer, subject, email, DateTimeOffset.Parse("2026-11-04T12:00:00Z")) : existing with { Role = "administrator" };
        accounts[account.UserId] = account;
        bootstrapAccount = account.UserId;
        return account;
    }

    public Task<IReadOnlyList<UserAccount>?> ListForAdministratorAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        AdministratorListRequest = (issuer, subject);
        return Task.FromResult(AdministratorListResult);
    }

    public Task<UserRoleChangeResult> ChangeRoleAsync(string actorIssuer, string actorSubject, Guid targetId, string role, string expectedRole, string reason, CancellationToken cancellationToken)
    {
        RoleChangeRequest = (actorIssuer, actorSubject, targetId, role, expectedRole, reason);
        RoleChangeCalls++;
        return Task.FromResult(RoleChangeResult);
    }

    public Task<UserProfileUpdateResult> UpdateProfileAsync(string issuer, string subject, string? displayName, string? address, CancellationToken cancellationToken)
    {
        ProfileUpdateCalls++;
        ProfileUpdateActor = (issuer, subject);
        var account = accounts.Values.SingleOrDefault(a => a.Issuer == issuer && a.Subject == subject);
        if (account is null) return Task.FromResult(new UserProfileUpdateResult("notfound", null));
        if (account.Status != "active") return Task.FromResult(new UserProfileUpdateResult("forbidden", null));
        if (!UserAccount.TryNormalizeProfile(displayName, address, out var name, out var place, out _)) return Task.FromResult(new UserProfileUpdateResult("invalid", null));
        return Task.FromResult(new UserProfileUpdateResult("updated", accounts[account.UserId] = account with { DisplayName = name, Address = place }));
    }
}
