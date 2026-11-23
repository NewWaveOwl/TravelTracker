using System.Globalization;

namespace TravelTracker.Frontend.Api.Routing;

/**
 * Exchanges address and route previews with the configured shared backend through the active session.
 * @param client Enforces trusted destinations, bearer handling and session revocation.
 */
public sealed class RoutingClient(ProtectedClient client)
{
    /**
     * Searches only after the traveller explicitly submits an address.
     * @param query The address to look up, without personal or confidential information.
     * @param cancellationToken Cancels a replaced query or closed view.
     * @return Reviewable suggestions, including a valid empty no-match result.
     */
    public async Task<AddressLookupResult> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
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
            result.Itineraries.Count > 10 || result.Itineraries.Any(route => route is null || string.IsNullOrWhiteSpace(route.Id) ||
                !double.IsFinite(route.DurationSeconds) || route.DurationSeconds < 0 || !double.IsFinite(route.DistanceMeters) || route.DistanceMeters < 0 ||
                route.TimingBasis is not ("scheduled" or "realtime" or "estimated") || route.Legs is null || route.Legs.Count > 100 ||
                route.Legs.Any(leg => leg is null || !double.IsFinite(leg.DurationSeconds) || leg.DurationSeconds < 0 ||
                    !double.IsFinite(leg.DistanceMeters) || leg.DistanceMeters < 0 || leg.ArrivalTime < leg.DepartureTime ||
                    leg.Geometry is null || leg.Geometry.Length > 200_000)))
            throw new ApiError(FailureKind.Unexpected, "The route service returned an unreadable response.");
        return result;
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
