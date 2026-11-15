using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Accommodations.Api.Services;

/**
 * Thrown when a candidate accommodation breaks the domain rules. The field errors stay keyed so the controller can answer with them.
 *
 * @param errors Field names mapped to their messages.
 */
public sealed class AccommodationValidationException(Dictionary<string, string[]> errors)
    : ValidationException("The accommodation contains invalid values.")
{
    /** Field names mapped to their messages, in the order the domain rules ran. */
    public Dictionary<string, string[]> Errors { get; } = errors;
}
