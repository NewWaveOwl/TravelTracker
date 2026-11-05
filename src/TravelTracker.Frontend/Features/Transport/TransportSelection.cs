using TravelTracker.Frontend.Api.Transport;

namespace TravelTracker.Frontend.Features.Transport;

/** One ordered draft use of saved catalogue records, not a scheduled journey or persisted trip leg. */
public sealed record TransportSelection(TransportDto Transport, RoutePointDto Departure, RoutePointDto Destination)
{
    public Guid OccurrenceId { get; init; } = Guid.NewGuid();
}
