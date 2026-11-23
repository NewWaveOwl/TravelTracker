using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Api.Routing;

/**
 * Exchanges address and route previews with the configured shared backend through the active session.
 * @param client Enforces trusted destinations, bearer handling and session revocation.
 */
public sealed class RoutingClient(ProtectedClient client)
{
    static readonly Dictionary<string, string> RouterModes = new()
    {
        ["pedestrian"] = "WALK", ["bike"] = "BICYCLE", ["car"] = "CAR", ["bus"] = "BUS", ["train"] = "RAIL",
    };

    /**
     * Reads the running router's supported modes and loaded service coverage.
     * @param cancellationToken Cancels a closed planner.
     * @return Live metadata, preserving unknown coverage rather than inventing bounds or dates.
     */
    public async Task<RoutingCapabilities> CapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.GetJsonAsync<RoutingCapabilities>("Routing", "/v1/routes/capabilities", cancellationToken);
        if (result is null || result.Status != "success" || result.Router is null || result.Coverage is null ||
            result.Modes is null || result.Modes.Count > 5 ||
            result.Modes.Any(mode => mode is null || string.IsNullOrEmpty(mode.Code) || !RouterModes.ContainsKey(mode.Code) || mode.Status is not ("available" or "unsupported" or "unknown")) ||
            result.Modes.Select(mode => mode.Code).Distinct().Count() != result.Modes.Count ||
            result.Coverage.Feeds is { Count: > 1000 } || result.Coverage.Feeds?.Any(feed => feed is null || feed.Modes is null || string.IsNullOrWhiteSpace(feed.Id)) == true)
            throw new ApiError(FailureKind.Unexpected, "The routing server returned unreadable capability information.");
        return result;
    }
    /**
     * Searches only after the traveller explicitly submits an address.
     * @param query The address to look up, without personal or confidential information.
     * @param cancellationToken Cancels a replaced query or closed view.
     * @return Reviewable suggestions, including a valid empty no-match result.
     */
    public async Task<AddressLookupResult> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = string.Join(" ", query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (query.Length is < 3 or > 200)
            throw new ApiError(FailureKind.Validation, "Enter an address between 3 and 200 characters.");
        return CheckAddresses(await client.GetJsonAsync<AddressLookupResult>("Routing", $"/v1/places/search?q={Uri.EscapeDataString(query)}", cancellationToken));
    }

    /**
     * Suggests an address once after a traveller places or moves a pin.
     * @param lat Selected latitude.
     * @param lon Selected longitude.
     * @param cancellationToken Cancels superseded marker work.
     * @return An editable suggestion, or a no-match result without moving the pin.
     */
    public async Task<AddressLookupResult> ReverseAsync(decimal lat, decimal lon, CancellationToken cancellationToken = default)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
            throw new ApiError(FailureKind.Validation, "Choose a valid map position.");
        var coordinates = FormattableString.Invariant($"lat={lat}&lon={lon}");
        return CheckAddresses(await client.GetJsonAsync<AddressLookupResult>("Routing", $"/v1/places/reverse?{coordinates}", cancellationToken));
    }

    /**
     * Requests candidate geometry for chosen endpoints, mode and departure time.
     * @param request The explicit travel choices.
     * @param cancellationToken Cancels changed endpoints or a closed preview.
     * @return Candidate routes without saving or replacing entered times.
     */
    public async Task<RoutePlanResult> PlanAsync(RoutePlanRequest request, CancellationToken cancellationToken = default)
    {
        var result = await client.SendJsonAsync<RoutePlanResult>("Routing", HttpMethod.Post, "/v1/routes/plan", request, cancellationToken);
        if (result is null || result.Status is not ("success" or "no_route" or "unsupported") || result.Itineraries is null ||
            result.Itineraries.Count > 10 || result.Status != "success" && result.Itineraries.Count != 0 ||
            result.Status == "success" && result.Itineraries.Count == 0 ||
            result.Itineraries.Any(route => !ValidRoute(route, request)))
            throw new ApiError(FailureKind.Unexpected, "The route service returned an unreadable response.");
        return result;
    }

    static bool ValidRoute(RouteItinerary? route, RoutePlanRequest request)
    {
        if (route is null || string.IsNullOrWhiteSpace(route.Id) || !double.IsFinite(route.DurationSeconds) || route.DurationSeconds <= 0 ||
            !double.IsFinite(route.DistanceMeters) || route.DistanceMeters < 0 || route.TransferCount is < 0 ||
            route.TimingBasis is not ("scheduled" or "realtime" or "estimated") || route.Legs is null || route.Legs.Count is < 1 or > 100 ||
            route.TransferCount is { } transfers && transfers > route.Legs.Count - 1 ||
            !RouterModes.TryGetValue(request.Mode, out var selectedMode)) return false;
        (decimal Lat, decimal Lon)? previousEnd = null;
        (decimal Lat, decimal Lon)? firstPoint = null;
        var previousArrival = request.DepartureTime;
        var selectedModePresent = false;
        foreach (var leg in route.Legs)
        {
            if (leg is null || !double.IsFinite(leg.DurationSeconds) || leg.DurationSeconds < 0 ||
                !double.IsFinite(leg.DistanceMeters) || leg.DistanceMeters < 0 || leg.ArrivalTime < leg.DepartureTime ||
                leg.DepartureTime < previousArrival || leg.Geometry is null || leg.Geometry.Length > 200_000 ||
                leg.Mode != selectedMode && !(request.Mode is "bus" or "train" && leg.Mode == "WALK")) return false;
            var points = RouteGeometry.Decode(leg.Geometry);
            if (points.Count < 2 || previousEnd is { } end && Meters(end, points[0]) > 250 ||
                leg.From is { } from && (!ValidStop(from) || Meters((from.Lat, from.Lon), points[0]) > 250) ||
                leg.To is { } to && (!ValidStop(to) || Meters((to.Lat, to.Lon), points[^1]) > 250) ||
                leg.IntermediateStops is { Count: > 1000 } || leg.IntermediateStops?.Any(stop => !ValidStop(stop)) == true) return false;
            selectedModePresent |= leg.Mode == selectedMode;
            firstPoint ??= points[0];
            previousEnd = points[^1];
            previousArrival = leg.ArrivalTime;
        }
        return selectedModePresent && firstPoint is { } startPoint && previousEnd is { } endPoint &&
            Meters(startPoint, (request.Origin.Lat, request.Origin.Lon)) <= 250 &&
            Meters(endPoint, (request.Destination.Lat, request.Destination.Lon)) <= 250 &&
            route.Legs[^1].ArrivalTime > route.Legs[0].DepartureTime &&
            (route.Legs[0].From is not { } origin || Meters((origin.Lat, origin.Lon), (request.Origin.Lat, request.Origin.Lon)) <= 250) &&
            (route.Legs[^1].To is not { } destination || Meters((destination.Lat, destination.Lon), (request.Destination.Lat, request.Destination.Lon)) <= 250);
    }

    static bool ValidStop(RouteStop? stop) => stop is not null && stop.Lat is >= -90 and <= 90 && stop.Lon is >= -180 and <= 180;

    static double Meters((decimal Lat, decimal Lon) from, (decimal Lat, decimal Lon) to)
    {
        const double radians = Math.PI / 180;
        var latitude = (double)(to.Lat - from.Lat) * radians;
        var longitude = (double)(to.Lon - from.Lon) * radians;
        var value = Math.Pow(Math.Sin(latitude / 2), 2) + Math.Cos((double)from.Lat * radians) *
            Math.Cos((double)to.Lat * radians) * Math.Pow(Math.Sin(longitude / 2), 2);
        return 12_742_000 * Math.Asin(Math.Sqrt(Math.Clamp(value, 0, 1)));
    }

    static AddressLookupResult CheckAddresses(AddressLookupResult? result)
    {
        if (result is null || result.Status is not ("success" or "no_match") || result.Candidates is null || result.Candidates.Count > 10 ||
            result.Candidates.Any(candidate => candidate is null || string.IsNullOrWhiteSpace(candidate.Label) || candidate.Label.Length > 2000 ||
                candidate.Lat is < -90 or > 90 || candidate.Lon is < -180 or > 180))
            throw new ApiError(FailureKind.Unexpected, "The address service returned an unreadable response.");
        return result;
    }
}
