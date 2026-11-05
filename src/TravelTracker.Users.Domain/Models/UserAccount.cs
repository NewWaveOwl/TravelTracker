using System.ComponentModel.DataAnnotations;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Domain.Models;

/**
 * Represents a Users-owned account and validates its intrinsic profile fields.
 */
public sealed record UserAccount
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public Guid PublicId { get; init; }
    public ProviderIdentity ProviderIdentity { get; init; }
    public UserRole Role { get; init; }
    public UserStatus Status { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /**
     * Creates an account with the supplied public identity and profile values.
     * @param publicId The public account UUID.
     * @param providerIdentity The validated provider issuer and subject.
     * @param role The server-assigned account role.
     * @param status The server-assigned account standing.
     * @param createdAt The account creation time.
     * @param username The optional username.
     * @param email The optional email address.
     * @param address The optional postal address.
     * @param updatedAt The time of the most recent profile update, if any.
     */
    public UserAccount(
        Guid publicId,
        ProviderIdentity providerIdentity,
        UserRole role,
        UserStatus status,
        DateTimeOffset createdAt,
        string? username = null,
        string? email = null,
        string? address = null,
        DateTimeOffset? updatedAt = null)
    {
        PublicId = publicId;
        ProviderIdentity = providerIdentity;
        Role = role;
        Status = status;
        CreatedAt = createdAt;
        Username = username;
        Email = email;
        Address = address;
        UpdatedAt = updatedAt;
    }

    /**
     * Provisions an active ordinary account without inferring profile fields.
     * @param providerIdentity The validated identity-provider issuer and subject.
     * @param createdAt The application clock's current time.
     * @return A new active account with a UUIDv7 public identifier.
     */
    public static UserAccount CreateTraveller(ProviderIdentity providerIdentity, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), providerIdentity, UserRole.User, UserStatus.Active, createdAt);

    /**
     * Creates an active administrator after the application authorizes bootstrap.
     * @param providerIdentity The validated identity-provider issuer and subject.
     * @param createdAt The application clock's current time.
     * @return A new administrator account with a UUIDv7 public identifier.
     */
    public static UserAccount CreateAdministrator(ProviderIdentity providerIdentity, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), providerIdentity, UserRole.Administrator, UserStatus.Active, createdAt);

    /**
     * Returns field errors for invalid account or profile values.
     * @return Validation errors keyed by the affected field; empty when valid.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var messages))
            {
                messages = [];
                errors[field] = messages;
            }

            messages.Add(message);
        }

        if (PublicId == Guid.Empty)
        {
            AddError("publicId", "A public account identifier is required.");
        }

        if (!ProviderIdentity.IsValid)
        {
            AddError("providerIdentity", "A provider issuer and subject are required.");
        }

        if (!Enum.IsDefined(Role))
        {
            AddError("role", "Unsupported account role.");
        }

        if (!Enum.IsDefined(Status))
        {
            AddError("status", "Unsupported account status.");
        }

        ValidateUsername(Username, AddError);
        ValidateEmail(Email, AddError);
        ValidateAddress(Address, AddError);

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void ValidateUsername(string? username, Action<string, string> addError)
    {
        if (username is null)
        {
            return;
        }

        if (username.Length is < 3 or > 32)
        {
            addError("username", "Username must contain between 3 and 32 characters.");
        }

        if (!string.Equals(username, username.Trim(), StringComparison.Ordinal))
        {
            addError("username", "Username must be trimmed.");
        }

        if (username.Any(character =>
                !(character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')))
        {
            addError("username", "Username may contain only ASCII letters, digits, '.', '_' or '-'.");
        }
    }

    private static void ValidateEmail(string? email, Action<string, string> addError)
    {
        if (email is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            addError("email", "Email must not be blank.");
        }

        if (email.Length > 254)
        {
            addError("email", "Email must not exceed 254 characters.");
        }

        if (!string.Equals(email, email.Trim(), StringComparison.Ordinal))
        {
            addError("email", "Email must be trimmed.");
        }

        if (!EmailAddressValidator.IsValid(email))
        {
            addError("email", "Email must be a valid email address.");
        }
    }

    private static void ValidateAddress(string? address, Action<string, string> addError)
    {
        if (address is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            addError("address", "Address must not be blank.");
        }

        if (address.Length > 500)
        {
            addError("address", "Address must not exceed 500 characters.");
        }

        if (!string.Equals(address, address.Trim(), StringComparison.Ordinal))
        {
            addError("address", "Address must be trimmed.");
        }
    }
}
