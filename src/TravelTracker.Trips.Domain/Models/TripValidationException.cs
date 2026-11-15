namespace TravelTracker.Trips.Domain.Models;

/**
 * thrown when a trip or an edit to it breaks the itinerary rules. The trip
 * the edit was applied to is never modified.
 *
 * @param errors every problem found, so the caller can show them next to their fields.
 */
public sealed class TripValidationException(IReadOnlyList<TripError> errors)
    : Exception(string.Join("; ", errors.Select(error => $"{error.Field}: {error.Message}")))
{
    /** every problem found, in the order the rules ran: trip fields first, then each leg in order. */
    public IReadOnlyList<TripError> Errors { get; } = errors;
}
