using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Api.Trips;

namespace TravelTracker.Frontend.Components.Pages;

/** Connects optional shared routing previews to the editable map without writing catalogue records. */
public partial class NewTrip
{
    RoutingClient? SharedRouting => Services.GetService<RoutingClient>();
    IReadOnlyList<TripPlaceDto> DraftLocationStops => PickedCoordinates is { Lat: >= -90 and <= 90, Lon: >= -180 and <= 180 } position
        ? [new(Guid.Empty, markerType ?? "attraction", string.IsNullOrWhiteSpace(placeName) ? "Selected location" : placeName,
            placeAddress, position.Lat, position.Lon)] : [];

    void PreviewManualPosition((decimal Lat, decimal Lon) position)
    {
        if (busy) return;
        (latitude, longitude, placeAddress) = (position.Lat, position.Lon, "");
        reverseAddressVersion = ++addressLookupSequence;
    }
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
