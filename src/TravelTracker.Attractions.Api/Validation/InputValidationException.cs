namespace TravelTracker.Attractions.Api.Validation;

/**
 * Carries field errors from application validation to the HTTP boundary.
 * @param fields." The fields." value.
 */
public sealed class InputValidationException(Dictionary<string, string[]> errors)
    : Exception("The request contains invalid fields.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}
