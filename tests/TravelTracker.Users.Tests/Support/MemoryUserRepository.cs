using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;
using ProfileRepository = TravelTracker.Users.Data.Repositories.Profile.IUserRepository;

namespace TravelTracker.Users.Tests.Support;

/** Test account fixture with scripted administration outcomes; real policy and persistence are verified against PostgreSQL. */
public sealed class MemoryUserRepository : IUserRepository, ProfileRepository
{
    readonly Dictionary<Guid, UserAccount> accounts = [];
    Guid? bootstrapAccount;
    public UserAccount? Account => bootstrapAccount is { } id ? accounts.GetValueOrDefault(id) : accounts.Values.FirstOrDefault();
    public int BootstrapCalls { get; private set; }
    public int ReadCalls { get; private set; }
    public bool StorageUnavailable { get; set; }
    public Exception? ProfileUpdateFailure { get; set; }
    public IReadOnlyList<UserAccount>? AdministratorListResult { get; set; }
    public UserRoleChangeResult RoleChangeResult { get; set; } = new("forbidden", null);
    public (string Issuer, string Subject)? AdministratorListRequest { get; private set; }
    public (string Issuer, string Subject, Guid TargetId, string Role, string ExpectedRole, string Reason)? RoleChangeRequest { get; private set; }
    public int RoleChangeCalls { get; private set; }

    /** Changes only the test account's standing to exercise the API's live read boundary. */
    public void Deactivate() => SetStatus(Account!.PublicId, UserStatus.Deactivated);

    public void SetStatus(Guid id, UserStatus status) => accounts[id] = accounts[id] with { Status = status };

    public Task<UserAccount?> FindAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        ReadCalls++;
        return Task.FromResult(accounts.Values.SingleOrDefault(a => a.ProviderIdentity == new ProviderIdentity(issuer, subject)));
    }

    public async Task<UserAccount> GetOrCreateTravellerAsync(string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(issuer, subject, cancellationToken);
        if (existing is not null) return existing;
        var account = UserAccount.CreateTraveller(new ProviderIdentity(issuer, subject), DateTimeOffset.Parse("2026-11-04T12:00:00Z")) with { VerifiedEmail = email };
        accounts[account.PublicId] = account;
        return account;
    }

    public async Task<UserAccount?> BootstrapAsync(string issuer, string subject, string email, CancellationToken cancellationToken)
    {
        BootstrapCalls++;
        var existing = await FindAsync(issuer, subject, cancellationToken);
        if (bootstrapAccount is { } id) return existing?.PublicId == id ? existing : null;
        if (accounts.Values.Any(a => a.Role == UserRole.Administrator) || existing?.Status is UserStatus.Banned or UserStatus.Deactivated) return null;
        var account = existing is null
            ? UserAccount.CreateAdministrator(new ProviderIdentity(issuer, subject), DateTimeOffset.Parse("2026-11-04T12:00:00Z"))
            : existing with { Role = UserRole.Administrator };
        account = account with { VerifiedEmail = email };
        accounts[account.PublicId] = account;
        bootstrapAccount = account.PublicId;
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

    public Task<UserAccount?> FindByProviderIdentityAsync(ProviderIdentity identity, CancellationToken cancellationToken)
    {
        if (StorageUnavailable) throw new InvalidOperationException("test-storage-details");
        return FindAsync(identity.Issuer, identity.Subject, cancellationToken);
    }

    public Task<UserAccount?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken)
    {
        ReadCalls++;
        return Task.FromResult(accounts.GetValueOrDefault(publicId));
    }

    public async Task<UserAccount> GetOrCreateAsync(ProviderIdentity identity, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        var existing = await FindByProviderIdentityAsync(identity, cancellationToken);
        if (existing is not null) return existing;
        var account = UserAccount.CreateTraveller(identity, createdAt);
        accounts[account.PublicId] = account;
        return account;
    }

    public async Task<UserAccount?> UpdateProfileAsync(
        ProviderIdentity identity, string? username, string? email, string? address, bool addressSpecified,
        DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var existing = await FindByProviderIdentityAsync(identity, cancellationToken);
        if (existing is null || existing.Status != UserStatus.Active) return existing;
        if (ProfileUpdateFailure is { } failure) throw failure;
        var account = existing with
        {
            Username = username ?? existing.Username,
            Email = email ?? existing.Email,
            Address = addressSpecified ? address : existing.Address,
            UpdatedAt = updatedAt
        };
        accounts[account.PublicId] = account;
        return account;
    }

}
