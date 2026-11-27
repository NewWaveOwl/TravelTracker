using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Transport;

namespace TravelTracker.Frontend.Api.Trips;

/**
 * Looks up and creates the public catalogue references supported by Trips.
 * @param attractions Access to saved attractions.
 * @param transport Access to reusable transport stops.
 * @param accommodations Access to saved accommodations, also used to create places from the map.
 */
public sealed class TripPlacesClient(AttractionsClient attractions, TransportClient transport, AccommodationsClient accommodations)
{
    /** the visit length sent for attractions created from the map, since the map draft collects none; users can edit it in the catalogue. */
    public const int DefaultVisitMinutes = 30;

    /**
     * Searches the catalogue corresponding to the selected marker palette item.
     * @param type Supported trip place type.
     * @param name Literal name search, or blank to list.
     * @param cancellationToken Cancels pending work.
     * @return Matching locations as trip place snapshots.
     */
    public async Task<IReadOnlyList<TripPlaceDto>> SearchAsync(string type, string name, CancellationToken cancellationToken) =>
        type switch
        {
            "attraction" => (await attractions.SearchAsync(name, cancellationToken)).Select(FromAttraction).ToArray(),
            "accommodation" => (await accommodations.SearchAsync(name, cancellationToken)).Select(FromAccommodation).ToArray(),
            "transport_stop" => (await transport.SearchPointsAsync(name, "transport_stop", cancellationToken)).Select(FromRoutePoint).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported trip place type.")
        };

    /**
     * Saves a draft map location in its own catalogue so Trips can reference it.
     * @param place New location with the selected marker type and coordinates.
     * @param cancellationToken Cancels pending work.
     * @return The location with its confirmed public identifier.
     */
    public async Task<TripPlaceDto> CreateAsync(TripPlaceDto place, CancellationToken cancellationToken)
    {
        switch (place.Type)
        {
            case "attraction":
                return FromAttraction(await attractions.CreateAsync(Fields(place, ("estimatedVisitMinutes", DefaultVisitMinutes)), cancellationToken));
            case "transport_stop":
                var fields = Fields(place, ("type", place.Type));
                fields["visibility"] = "private";
                return FromRoutePoint(await transport.CreatePointAsync(fields, cancellationToken));
            case "accommodation":
                return FromAccommodation(await accommodations.CreateAsync(new CreateAccommodationRequest(place.Name, place.Address, place.Lat, place.Lon, "private"), cancellationToken));
            default:
                throw new ArgumentOutOfRangeException(nameof(place), place.Type, "Unsupported trip place type.");
        }
    }

    static Dictionary<string, object?> Fields(TripPlaceDto place, (string Key, object Value) extra) => new()
    {
        ["name"] = place.Name,
        ["address"] = place.Address,
        ["lat"] = place.Lat,
        ["lon"] = place.Lon,
        [extra.Key] = extra.Value
    };

    static TripPlaceDto FromAttraction(AttractionDto place) =>
        new(place.AttractionId, "attraction", place.Name, place.Address, place.Lat, place.Lon);

    static TripPlaceDto FromAccommodation(AccommodationDto place) =>
        new(place.AccommodationId, "accommodation", place.Name, place.Address, place.Lat, place.Lon);

    static TripPlaceDto FromRoutePoint(RoutePointDto place) =>
        new(place.RoutePointId, "transport_stop", place.Name, place.Address, place.Lat, place.Lon);
}
