using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * The result of reading one trip for its owner. Trip is set only when Status is Found.
 *
 * @param Status Outcome category.
 * @param Trip The stored trip, or null.
 */
public sealed record TripReadOutcome(TripReadStatus Status, Trip? Trip);
