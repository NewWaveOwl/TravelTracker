using System.ComponentModel.DataAnnotations;
using TravelTracker.Frontend.Api.Users;

namespace TravelTracker.Frontend.Features.Users;

/** Editable values initialized from the server profile; role and status are intentionally absent. */
public sealed class ProfileEditModel : IValidatableObject
{
    readonly SelfUserProfile original;

    /**
     * Creates an editable form model from the confirmed caller profile.
     * @param profile The server-confirmed profile used as the unchanged-field baseline.
     * @return A form model containing only the editable public fields.
     */
    public ProfileEditModel(SelfUserProfile profile)
    {
        original = profile;
        Username = profile.Username ?? "";
        Email = profile.Email ?? "";
        Address = profile.Address ?? "";
    }

    public string Username { get; set; }

    public string Email { get; set; }

    [StringLength(500, ErrorMessage = "Address must be 500 characters or fewer.")]
    public string Address { get; set; }

    public bool HasChanges => !string.Equals(Normalize(Username), Normalize(original.Username), StringComparison.Ordinal)
        || !string.Equals(Normalize(Email), Normalize(original.Email), StringComparison.OrdinalIgnoreCase)
        || !string.Equals(string.IsNullOrWhiteSpace(Address) ? null : Address.Trim(), original.Address, StringComparison.Ordinal);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var username = Normalize(Username);
        var oldUsername = Normalize(original.Username);
        if (!string.Equals(username, oldUsername, StringComparison.Ordinal))
        {
            if (username is null) yield return new ValidationResult("Username is required.", [nameof(Username)]);
            else if (username.Length is < 3 or > 32) yield return new ValidationResult("Username must be 3–32 characters.", [nameof(Username)]);
            else if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[A-Za-z0-9._-]+$")) yield return new ValidationResult("Use only letters, numbers, periods, underscores or hyphens.", [nameof(Username)]);
        }

        var email = Normalize(Email);
        var oldEmail = Normalize(original.Email);
        if (!string.Equals(email, oldEmail, StringComparison.OrdinalIgnoreCase))
        {
            if (email is null) yield return new ValidationResult("Contact email cannot be removed.", [nameof(Email)]);
            else if (email.Length > 254) yield return new ValidationResult("Contact email must be 254 characters or fewer.", [nameof(Email)]);
            else if (!new EmailAddressAttribute().IsValid(email)) yield return new ValidationResult("Enter a valid contact email.", [nameof(Email)]);
        }

        if (Address.Length > 500) yield return new ValidationResult("Address must be 500 characters or fewer.", [nameof(Address)]);
    }

    static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
