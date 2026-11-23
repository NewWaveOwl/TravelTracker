using TravelTracker.Users.Data.Repositories.Credentials;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Tests.Support;

/** In-memory credential store for host tests; real persistence is verified against PostgreSQL. */
public sealed class MemoryCredentialRepository : ICredentialRepository
{
    readonly List<(string Login, StoredCredential Credential)> entries = [];
    public int Calls { get; private set; }
    public bool Unavailable { get; set; }

    /** The stored credential for a login. */
    public StoredCredential For(string login) => Find(login)!;

    /** Adds a credential the way registration would, without hashing, for tests that only need a login to exist. */
    public void Seed(string login, string issuer, string subject, string passwordHash = "unused") => entries.Add((login, new StoredCredential(
        new UserAccount(Guid.CreateVersion7(), new ProviderIdentity(issuer, subject), UserRole.User, UserStatus.Active, DateTimeOffset.UtcNow, username: login), passwordHash)));

    /** Removes every credential. */
    public void Unregister() => entries.Clear();

    /** Changes the account standing for a login. */
    public void SetStatus(string login, UserStatus status)
    {
        var index = entries.FindIndex(entry => entry.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
        entries[index] = (entries[index].Login, entries[index].Credential with { Account = entries[index].Credential.Account with { Status = status } });
    }

    public Task<UserAccount?> RegisterAsync(string issuer, string login, string passwordHash, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        Calls++;
        if (Find(login) is not null) return Task.FromResult<UserAccount?>(null);
        var id = Guid.CreateVersion7();
        var account = new UserAccount(id, new ProviderIdentity(issuer, id.ToString("D")), UserRole.User, UserStatus.Active, createdAt, username: login);
        entries.Add((login, new StoredCredential(account, passwordHash)));
        return Task.FromResult<UserAccount?>(account);
    }

    public Task<StoredCredential?> FindAsync(string login, CancellationToken cancellationToken)
    {
        Calls++;
        // the message stands in for a provider diagnostic that must never reach a client
        if (Unavailable) throw new InvalidOperationException("credential-store-secret-diagnostic");
        return Task.FromResult(Find(login));
    }

    public Task<StoredCredential?> FindByIdentityAsync(ProviderIdentity identity, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(entries.Select(entry => entry.Credential)
            .FirstOrDefault(credential => credential.Account.ProviderIdentity == identity));
    }

    public Task UpdateHashAsync(Guid publicId, string passwordHash, CancellationToken cancellationToken)
    {
        var index = entries.FindIndex(entry => entry.Credential.Account.PublicId == publicId);
        entries[index] = (entries[index].Login, entries[index].Credential with { PasswordHash = passwordHash });
        return Task.CompletedTask;
    }

    StoredCredential? Find(string login) => entries.FirstOrDefault(entry => entry.Login.Equals(login.Trim(), StringComparison.OrdinalIgnoreCase)).Credential;
}
