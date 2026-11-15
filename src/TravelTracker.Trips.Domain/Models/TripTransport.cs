namespace TravelTracker.Trips.Domain.Models;

/**
 * snapshot of the transport mode a leg uses, copied when the trip is saved.
 *
 * @param TransportModeId public identifier of the transport mode in the Transport service.
 * @param Code stable machine code at save time, such as car or train.
 * @param Name display name at save time.
 */
public sealed record TripTransport(Guid TransportModeId, string Code, string Name);
