using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using TravelTracker.Frontend.Api.Users;

namespace TravelTracker.Frontend.Features.Users;

/** Editable values initialized from the server profile; role and status are intentionally absent. */
public sealed partial class ProfileEditModel
{
    readonly SelfUserProfile original;

    /**
     * Creates an editable form model from the confirmed caller profile.
     * @param profile The server-confirmed profile used as the unchanged-field baseline.
     */
    public ProfileEditModel(SelfUserProfile profile)
    {
        original = profile;
        Username = profile.Username ?? "";
        Email = profile.Email ?? "";
        Address = profile.Address ?? "";
    }

    [CustomValidation(typeof(ProfileEditModel), nameof(ValidateUsername))]
    public string Username { get; set; }

    [CustomValidation(typeof(ProfileEditModel), nameof(ValidateEmail))]
    public string Email { get; set; }

    [StringLength(500, ErrorMessage = "Address must be 500 characters or fewer.")]
    public string Address { get; set; }

    /** true when any editable field differs from the confirmed profile, using the same rules as the PATCH body. */
    public bool HasChanges => new UpdateSelfProfileRequest(Username, Email, Address).ChangedFields(original).Count > 0;

    /**
     * Validates a changed username as a field so the form can show its error beside the input.
     * @param value The proposed username value.
     * @param validationContext Supplies the model's original profile for unchanged-field handling.
     * @return A field-associated validation error, or success when the value is allowed.
     */
    public static ValidationResult? ValidateUsername(string? value, ValidationContext validationContext)
    {
        var model = (ProfileEditModel)validationContext.ObjectInstance;
        var username = Normalize(value);
        var oldUsername = Normalize(model.original.Username);
        if (!string.Equals(username, oldUsername, StringComparison.Ordinal))
        {
            if (username is null) return new ValidationResult("Username is required.", [nameof(Username)]);
            if (username.Length is < 3 or > 32) return new ValidationResult("Username must be 3 to 32 characters.", [nameof(Username)]);
            if (!UsernameCharacters().IsMatch(username))
                return new ValidationResult("Use only letters, numbers, periods, underscores or hyphens.", [nameof(Username)]);
        }
        return ValidationResult.Success;
    }

    /**
     * Validates a changed contact email as a field while allowing an untouched blank initial value.
     * @param value The proposed contact email value.
     * @param validationContext Supplies the model's original profile for unchanged-field handling.
     * @return A field-associated validation error, or success when the value is allowed.
     */
    public static ValidationResult? ValidateEmail(string? value, ValidationContext validationContext)
    {
        var model = (ProfileEditModel)validationContext.ObjectInstance;
        var email = Normalize(value);
        var oldEmail = Normalize(model.original.Email);
        if (!string.Equals(email, oldEmail, StringComparison.Ordinal))
        {
            if (email is null) return new ValidationResult("Contact email cannot be removed.", [nameof(Email)]);
            if (email.Length > 254) return new ValidationResult("Contact email must be 254 characters or fewer.", [nameof(Email)]);
            if (!new EmailAddressAttribute().IsValid(email))
                return new ValidationResult("Enter a valid contact email.", [nameof(Email)]);
        }
        return ValidationResult.Success;
    }

    static string? Normalize(string? value) => UpdateSelfProfileRequest.Normalize(value);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex UsernameCharacters();
}
