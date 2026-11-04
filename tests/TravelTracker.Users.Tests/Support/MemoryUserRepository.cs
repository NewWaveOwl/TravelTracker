using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Tests.Support;

/** Sequential test-only repository; real PostgreSQL locking/durability is verified separately. */
public sealed class MemoryUserRepository : IUserRepository
{
    readonly Dictionary<Guid, UserAccount> accounts = [];
    Guid? bootstrapAccount;
    public UserAccount? Account => bootstrapAccount is { } id ? accounts.GetValueOrDefault(id) : accounts.Values.FirstOrDefault();
    public int BootstrapCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public int RoleChanges { get; private set; }

    /** Changes only the test account's standing to exercise the API's live read boundary. */
    public void Deactivate() => SetStatus(Account!.UserId, "deactivated");

    public void SetStatus(Guid id, string status) => accounts[id] = accounts[id] with { Status = status };
    public void Seed(UserAccount account) => accounts[account.UserId] = account;

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

    public Task<IReadOnlyList<UserAccount>?> ListForAdministratorAsync(string issuer, string subject, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserAccount>?>(ActiveAdministrator(issuer, subject) ? accounts.Values.OrderBy(a => a.Email, StringComparer.OrdinalIgnoreCase).ToList() : null);

    public Task<UserRoleChangeResult> ChangeRoleAsync(string actorIssuer, string actorSubject, Guid targetId, string role, string expectedRole, string reason, CancellationToken cancellationToken)
    {
        UserRoleChangeResult Result(string code, UserAccount? account = null) => new(code, account);
        if (!ActiveAdministrator(actorIssuer, actorSubject)) return Task.FromResult(Result("forbidden"));
        if (!accounts.TryGetValue(targetId, out var target)) return Task.FromResult(Result("notfound"));
        if (role is not ("administrator" or "traveller") || expectedRole is not ("administrator" or "traveller") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || role == "administrator" && target.Status != "active") return Task.FromResult(Result("invalid"));
        if (target.Role != expectedRole || role == target.Role) return Task.FromResult(Result("conflict"));
        if (target.Role == "administrator" && target.Status == "active" && role == "traveller" && accounts.Values.Count(a => a.Role == "administrator" && a.Status == "active") == 1) return Task.FromResult(Result("last-administrator"));
        var updated = target with { Role = role };
        accounts[targetId] = updated; RoleChanges++;
        return Task.FromResult(Result("changed", updated));
    }

    bool ActiveAdministrator(string issuer, string subject) => accounts.Values.Any(a => a.Issuer == issuer && a.Subject == subject && a.Role == "administrator" && a.Status == "active");
}
