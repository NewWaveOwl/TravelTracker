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
 * @param DisplayName Owner-editable name shown in the UI; defaults to the email's local part.
 * @param Address Optional owner-editable free text; never used for identity.
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
    public string DisplayName { get; init; }
    public string? Address { get; init; }

    public const int DisplayNameMaxLength = 60;
    public const int AddressMaxLength = 200;

    public UserAccount(Guid UserId, string Issuer, string Subject, string Email, string Role, string Status, DateTimeOffset CreatedAt, string? DisplayName = null, string? Address = null)
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
        if (!TryNormalizeProfile(DisplayName ?? DefaultDisplayName(Email), Address, out var name, out var place, out _))
            throw new ArgumentException("Invalid profile fields.", nameof(DisplayName));
        this.DisplayName = name;
        this.Address = place;
    }

    /**
     * Derives the initial display name so every account has one without asking the provider for a profile.
     * @param email The verified email.
     * @return The email's local part cut to the display name limit, or "Traveller" when that is empty.
     */
    public static string DefaultDisplayName(string email)
    {
        var local = email.Split('@')[0].Trim();
        local = local.Length > DisplayNameMaxLength ? local[..DisplayNameMaxLength] : local;
        return local.Any(char.IsControl) || local.Length == 0 ? "Traveller" : local;
    }

    /**
     * Validates and normalizes the owner-editable profile fields in one place for the API and the domain.
     * @param displayName Requested name; trimmed, 1 to 60 characters, no control characters.
     * @param address Requested address; trimmed, blank becomes null, at most 200 characters, newlines allowed.
     * @param name The normalized display name when valid.
     * @param place The normalized address (null when cleared) when valid.
     * @param errors Field name to messages when invalid.
     * @return Whether both fields are valid.
     */
    public static bool TryNormalizeProfile(string? displayName, string? address, out string name, out string? place, out Dictionary<string, string[]> errors)
    {
        errors = [];
        name = displayName?.Trim() ?? "";
        place = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        if (name.Length is 0 or > DisplayNameMaxLength || name.Any(char.IsControl))
            errors["displayName"] = [$"Enter a display name of 1 to {DisplayNameMaxLength} characters without control characters."];
        if (place is not null && (place.Length > AddressMaxLength || place.Any(c => char.IsControl(c) && c != '\n')))
            errors["address"] = [$"Enter an address of at most {AddressMaxLength} characters without control characters."];
        return errors.Count == 0;
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
