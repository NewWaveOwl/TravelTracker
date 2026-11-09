namespace TravelTracker.Frontend.Api.Trips;

/**
 * An editable itinerary and the entity tag from the same response.
 * @param Trip Saved values displayed in the editor.
 * @param ETag Entity tag required to replace this version.
 */
public sealed record TripRevision(TripDto Trip, string ETag);
