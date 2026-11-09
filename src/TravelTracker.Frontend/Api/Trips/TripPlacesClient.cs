using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Transport;

namespace TravelTracker.Frontend.Api.Trips;

/**
 * Looks up and creates the public catalogue references supported by Trips.
 * @param attractions Access to saved attractions.
 * @param transport Access to reusable transport stops.
 * @param client Access to the independent accommodations catalogue.
 */
public sealed class TripPlacesClient(AttractionsClient attractions, TransportClient transport, ProtectedClient client)
{
    const string AccommodationsPath = "/api/v1/accommodations";

    /**
     * Searches the catalogue corresponding to the selected marker palette item.
     * @param type Supported trip place type.
     * @param name Literal name search, or blank to list.
     * @param token Cancels pending work.
     * @return Matching locations as trip place snapshots.
     */
    public async Task<IReadOnlyList<TripPlaceDto>> SearchAsync(string type, string name, CancellationToken token) =>
        type switch
        {
            "attraction" => (await attractions.SearchAsync(name, token)).Select(FromAttraction).ToArray(),
            "accommodation" => (await client.GetJsonAsync<List<Accommodation>>("Accommodations",
                $"{AccommodationsPath}?name={Uri.EscapeDataString(name.Trim())}", token)
                ?? throw new ApiError(FailureKind.Unexpected, "The accommodations catalogue returned no results.")).Select(FromAccommodation).ToArray(),
            _ => (await transport.SearchPointsAsync(name, "transport_stop", token)).Select(FromRoutePoint).ToArray()
        };

    /**
     * Saves a draft map location in its own catalogue so Trips can reference it.
     * @param place New location with the selected marker type and coordinates.
     * @param token Cancels pending work.
     * @return The location with its confirmed public identifier.
     */
    public async Task<TripPlaceDto> CreateAsync(TripPlaceDto place, CancellationToken token)
    {
        var fields = new Dictionary<string, object?>
        {
            ["name"] = place.Name,
            ["address"] = place.Address,
            ["lat"] = place.Lat,
            ["lon"] = place.Lon
        };
        switch (place.Type)
        {
            case "attraction":
                fields["estimatedVisitMinutes"] = 30;
                return FromAttraction(await attractions.CreateAsync(fields, token));
            case "accommodation":
                return FromAccommodation(await client.SendJsonAsync<Accommodation>("Accommodations", HttpMethod.Post,
                    AccommodationsPath, fields, token)
                    ?? throw new ApiError(FailureKind.Unexpected, "The accommodation save could not be confirmed."));
            default:
                fields["type"] = "transport_stop";
                return FromRoutePoint(await transport.CreatePointAsync(fields, token));
        }
    }

    static TripPlaceDto FromAttraction(AttractionDto place) =>
        new(place.AttractionId, "attraction", place.Name, place.Address, place.Lat, place.Lon);

    static TripPlaceDto FromAccommodation(Accommodation place) =>
        new(place.AccommodationId, "accommodation", place.Name, place.Address, place.Lat, place.Lon);

    static TripPlaceDto FromRoutePoint(RoutePointDto place) =>
        new(place.RoutePointId, "transport_stop", place.Name, place.Address, place.Lat, place.Lon);

    sealed record Accommodation(Guid AccommodationId, string Name, string Address, decimal Lat, decimal Lon);
}
