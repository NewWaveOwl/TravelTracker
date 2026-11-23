using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Api.Routing;

namespace TravelTracker.Frontend.Components.Pages;

/** Connects optional shared routing previews to the editable map without writing catalogue records. */
public partial class NewTrip
{
    RoutingClient? SharedRouting => Services.GetService<RoutingClient>();
    RouteItinerary? previewRoute;
    RoutePlanRequest? previewRequest;

    RoutePlanRequest? CurrentRouteRequest => CurrentLeg is { Mode: { } mode } leg && selected > 0 &&
        draft.DepartureFor(leg) is { } departure
        ? new(new(draft.Stops[selected - 1].Place.Lat, draft.Stops[selected - 1].Place.Lon),
            new(draft.Stops[selected].Place.Lat, draft.Stops[selected].Place.Lon), mode.Code, departure) : null;

    RouteItinerary? MapRoutePreview => CurrentRouteRequest is { } request && request == previewRequest ? previewRoute : null;

    void SelectRoutePreview(RouteItinerary? route)
    {
        previewRequest = CurrentRouteRequest;
        previewRoute = route;
    }

    /**
     * Previews the selected search candidate without saving or changing the existing marker.
     * @param candidate The address explicitly selected by the traveller.
     */
    void PreviewAddress(AddressCandidate candidate)
    {
        (latitude, longitude, placeAddress) = (candidate.Lat, candidate.Lon, candidate.Label);
        reverseAddressVersion = 0;
    }
}
