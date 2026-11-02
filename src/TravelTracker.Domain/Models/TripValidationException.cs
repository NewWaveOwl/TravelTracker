namespace TravelTracker.Domain.Models;

/**
 * Thrown when a trip or an edit to it breaks the itinerary rules. The trip
 * the edit was applied to is never modified.
 *
 * @param errors Every problem found, so the caller can show them next to their fields.
 */
public sealed class TripValidationException(IReadOnlyList<TripError> errors)
    : Exception(string.Join("; ", errors.Select(error => $"{error.Field}: {error.Message}"))) {
    /** Every problem found, in leg order. */
    public IReadOnlyList<TripError> Errors { get; } = errors;
}
