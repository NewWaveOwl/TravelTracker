using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * The result of reading one trip for its owner. Trip is set only when Status is Found.
 *
 * @param Status Outcome category.
 * @param Trip The stored trip, or null.
 * @param Detail Safe diagnostic text; never identities, tokens or storage messages.
 */
public sealed record TripReadOutcome(TripReadStatus Status, Trip? Trip, string Detail);
