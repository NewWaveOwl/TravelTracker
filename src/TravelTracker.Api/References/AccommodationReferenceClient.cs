using TravelTracker.Domain.Models;

namespace TravelTracker.Api.References;

/**
 * Reads accommodations from the public Accommodations contract under the caller's own token,
 * so private places owned by someone else come back as not found.
 *
 * @param http Typed client whose base address points at the Accommodations service.
 * @param sender Sends the request and logs failures.
 */
public sealed class AccommodationReferenceClient(HttpClient http, ReferenceSender sender) {
    /**
     * Reads one accommodation the caller may see and copies it into a place snapshot.
     *
     * @param accommodationId Public accommodation identifier.
     * @param bearerToken The caller's access token, forwarded to the catalogue unchanged.
     * @param cancellationToken Cancels the lookup.
     * @return A place snapshot when found, otherwise the non-found outcome.
     */
    public async Task<ReferenceResult<TripPlace>> GetPlaceAsync(Guid accommodationId, string bearerToken, CancellationToken cancellationToken) {
        var result = await sender.SendAsync<AccommodationResponse>(http,
            ReferenceRequest.Get($"api/v1/accommodations/{accommodationId:D}", bearerToken), $"accommodation {accommodationId:D} lookup", cancellationToken);
        return result.Status == ReferenceStatus.Found
            ? ReferenceResult<TripPlace>.Found(new(accommodationId, "accommodation", result.Value!.Name, result.Value.Address, result.Value.Lat, result.Value.Lon))
            : result.Forward<TripPlace>();
    }

    /** The fields of the Accommodations response that a trip snapshot keeps. */
    private sealed record AccommodationResponse(string Name, string Address, decimal Lat, decimal Lon);
}
