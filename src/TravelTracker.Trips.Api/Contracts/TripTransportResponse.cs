namespace TravelTracker.Trips.Api.Contracts;

/**
 * Public snapshot of the transport mode a leg used when the trip was saved.
 *
 * @param TransportModeId Public identifier of the transport mode.
 * @param Code Stable machine code, such as car or train.
 * @param Name Display name at save time.
 */
public sealed record TripTransportResponse(Guid TransportModeId, string Code, string Name);
