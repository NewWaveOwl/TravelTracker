using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Frontend.Features.Users;

/** Form values for a password change; the Users API enforces the same length rule again. */
public sealed class PasswordChangeModel
{
    /** Shortest accepted password, in characters. */
    public const int MinimumLength = 6;

    /** Longest accepted password, in characters. */
    public const int MaximumLength = 128;

    [Required(ErrorMessage = "Enter your current password.")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(MaximumLength, MinimumLength = MinimumLength, ErrorMessage = "Use 6 to 128 characters for the new password.")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and its confirmation do not match.")]
    public string ConfirmPassword { get; set; } = "";
}
