using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * The result of checking a draft trip. Trip is set only when Status is Ready.
 *
 * @param Status Outcome category.
 * @param Trip The snapshot trip ready to save, or null.
 * @param Errors Problems tied to the field that caused them; empty when Ready.
 * @param Detail Safe diagnostic text; never URLs, tokens, response bodies or identities.
 */
public sealed record TripReferenceOutcome(TripReferenceStatus Status, Trip? Trip, IReadOnlyList<TripError> Errors, string Detail);
