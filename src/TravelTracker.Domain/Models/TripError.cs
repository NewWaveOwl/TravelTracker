namespace TravelTracker.Domain.Models;

/**
 * One actionable problem found in a trip, tied to the field that caused it.
 *
 * @param Field Path of the offending field, such as "legs[1].departure".
 * @param Message What is wrong and how to fix it.
 */
public sealed record TripError(string Field, string Message);
