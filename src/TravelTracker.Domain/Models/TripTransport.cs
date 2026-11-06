namespace TravelTracker.Domain.Models;

/**
 * Snapshot of the transport mode a leg uses, copied when the trip is saved.
 *
 * @param TransportModeId Public identifier of the transport mode in the Transport service.
 * @param Code Stable machine code at save time, such as car or train.
 * @param Name Display name at save time.
 */
public sealed record TripTransport(Guid TransportModeId, string Code, string Name);
