namespace TravelTracker.Users.Domain.Models;

/**
 * A service-owned account bound to a provider identity; role assignment never comes from profile input.
 * @param UserId The public account identifier.
 * @param Issuer The already-validated identity provider issuer.
 * @param Subject The already-validated subject within that issuer.
 * @param Email The verified provider email recorded at provisioning.
 * @param Role The server-assigned traveller or administrator role.
 * @param Status The server-assigned active, banned or deactivated standing.
 * @param CreatedAt The account creation time supplied by the application clock.
 */
public sealed record UserAccount
{
    public Guid UserId { get; init; }
    public string Issuer { get; init; }
    public string Subject { get; init; }
    public string Email { get; init; }
    public string Role { get; init; }
    public string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public UserAccount(Guid UserId, string Issuer, string Subject, string Email, string Role, string Status, DateTimeOffset CreatedAt)
    {
        if (UserId == Guid.Empty) throw new ArgumentException("A public account identifier is required.", nameof(UserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(Email);
        if (Role is not ("traveller" or "administrator")) throw new ArgumentException("Unsupported account role.", nameof(Role));
        if (Status is not ("active" or "banned" or "deactivated")) throw new ArgumentException("Unsupported account status.", nameof(Status));
        this.UserId = UserId;
        this.Issuer = Issuer;
        this.Subject = Subject;
        this.Email = Email;
        this.Role = Role;
        this.Status = Status;
        this.CreatedAt = CreatedAt;
    }

    /**
     * Provisions an ordinary active account from an identity validated by the application boundary.
     * @param issuer The validated provider issuer.
     * @param subject The validated subject.
     * @param email The verified email.
     * @param createdAt The application clock's current time.
     * @return A new active traveller with a generated public UUIDv7 identifier.
     */
    public static UserAccount CreateTraveller(string issuer, string subject, string email, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), issuer, subject, email, "traveller", "active", createdAt);

    /**
     * Constructs the first administrator only after the application authorizes bootstrap.
     * @param issuer The validated provider issuer.
     * @param subject The validated subject.
     * @param email The verified email matching the configured bootstrap target.
     * @param createdAt The application clock's current time.
     * @return A new active administrator with a generated public UUIDv7 identifier.
     */
    public static UserAccount CreateAdministrator(string issuer, string subject, string email, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), issuer, subject, email, "administrator", "active", createdAt);
}
