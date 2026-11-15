using TravelTracker.Frontend.Api.Transport;

namespace TravelTracker.Frontend.Features.Transport;

/**
 * One ordered draft use of saved catalogue records, not a scheduled journey or persisted trip leg.
 * @param Transport The saved transport mode.
 * @param Departure The saved departure location.
 * @param Destination The saved destination location.
 */
public sealed record TransportSelection(TransportDto Transport, RoutePointDto Departure, RoutePointDto Destination)
{
    /** identifies this use of the records, so the same pair can appear twice in one draft. */
    public Guid OccurrenceId { get; init; } = Guid.NewGuid();
    public int? TravelMinutes { get; init; }
    public int? ChangeMinutes { get; init; }
}
