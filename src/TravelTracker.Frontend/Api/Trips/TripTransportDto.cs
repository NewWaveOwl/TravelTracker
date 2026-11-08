namespace TravelTracker.Frontend.Api.Trips;

/** The transport mode saved on a trip leg. */
public sealed record TripTransportDto(Guid TransportModeId, string Code, string Name);
