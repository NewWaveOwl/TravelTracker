namespace TravelTracker.Trips.Api.Contracts;

/**
 * public snapshot of the transport mode a leg used when the trip was saved.
 *
 * @param TransportModeId public identifier of the transport mode.
 * @param Code stable machine code, such as car or train.
 * @param Name display name at save time.
 */
public sealed record TripTransportResponse(Guid TransportModeId, string Code, string Name);
