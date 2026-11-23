namespace TravelTracker.Trips.Domain.Models;

/**
 * one actionable problem found in a trip, tied to the field that caused it.
 *
 * @param Field path of the offending field, such as "legs[1].departure".
 * @param Message what is wrong and how to fix it.
 */
public sealed record TripError(string Field, string Message);
