using TravelTracker.Frontend.Api.Transport;

namespace TravelTracker.Frontend.Api.Routing;

/** One explicit preview or confirmation tied to specific stop occurrences and a saved catalogue mode. */
public sealed record RouteSelection(RoutePlanRequest Request, RouteItinerary Itinerary, TransportDto Transport,
    int OriginIndex, int DestinationIndex);
