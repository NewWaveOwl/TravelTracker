namespace TravelTracker.Frontend.Api.Trips;

/** A saved trip as the Trips service returns it. */
public sealed record TripDto(Guid TripId, string Name, string TimeZone, DateTimeOffset Start, DateTimeOffset End, bool IsRoundTrip, IReadOnlyList<TripLegDto> Legs);
