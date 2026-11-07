using Microsoft.Extensions.Logging;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.References;

/**
 * Reads Transport route points from the public Transport contract. Only transport stops are
 * owned by Transport; accommodations and attractions are read from their own services.
 *
 * @param http Typed client whose base address points at the Transport service.
 * @param sender Sends the request and logs failures.
 * @param logger Records a route point whose type is not a transport stop.
 */
public sealed class RoutePointReferenceClient(HttpClient http, ReferenceSender sender, ILogger<RoutePointReferenceClient> logger) {
    /**
     * Reads one transport stop route point and copies it into a place snapshot.
     *
     * @param routePointId Public route point identifier.
     * @param bearerToken The caller's access token, forwarded to the catalogue unchanged.
     * @param cancellationToken Cancels the lookup.
     * @return A place snapshot when the record is a transport stop, otherwise the non-found outcome.
     */
    public async Task<ReferenceResult<TripPlace>> GetTransportStopAsync(Guid routePointId, string bearerToken, CancellationToken cancellationToken) {
        var operation = $"route point {routePointId:D} lookup";
        var result = await sender.SendAsync<RoutePointResponse>(http,
            ReferenceRequest.Get($"api/v1/route-points/{routePointId:D}", bearerToken), operation, cancellationToken);
        if (result.Status != ReferenceStatus.Found) return result.Forward<TripPlace>();
        var point = result.Value!;
        if (point.Type == "transport_stop")
            return string.IsNullOrWhiteSpace(point.Name) || point.Address is null || point.Lat is null || point.Lon is null
                ? sender.Incomplete<TripPlace>(operation)
                : ReferenceResult<TripPlace>.Found(new(routePointId, point.Type, point.Name, point.Address, point.Lat.Value, point.Lon.Value));
        logger.LogWarning("route point {RoutePointId} is a {Type}, not a transport stop; treated as absent.", routePointId, point.Type);
        return ReferenceResult<TripPlace>.Failed(ReferenceStatus.NotFound, "upstream record is not a transport stop.");
    }

    /** The fields of the Transport route point response that a trip snapshot keeps. */
    private sealed record RoutePointResponse(string? Type, string? Name, string? Address, decimal? Lat, decimal? Lon);
}
