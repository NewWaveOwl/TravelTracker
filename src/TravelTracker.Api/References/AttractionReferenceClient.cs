using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.References;

/**
 * Reads attractions from the public Attractions contract under the caller's own token,
 * so private attractions owned by someone else come back as not found.
 *
 * @param http Typed client whose base address points at the Attractions service.
 * @param sender Sends the request and logs failures.
 */
public sealed class AttractionReferenceClient(HttpClient http, ReferenceSender sender) {
    /**
     * Reads one attraction the caller may see and copies it into a place snapshot.
     *
     * @param attractionId Public attraction identifier.
     * @param bearerToken The caller's access token, forwarded to the catalogue unchanged.
     * @param cancellationToken Cancels the lookup.
     * @return A place snapshot when found, otherwise the non-found outcome.
     */
    public async Task<ReferenceResult<TripPlace>> GetPlaceAsync(Guid attractionId, string bearerToken, CancellationToken cancellationToken) {
        var result = await sender.SendAsync<AttractionResponse>(http,
            ReferenceRequest.Get($"api/v1/attractions/{attractionId:D}", bearerToken), $"attraction {attractionId:D} lookup", cancellationToken);
        return result.Status == ReferenceStatus.Found
            ? ReferenceResult<TripPlace>.Found(new(attractionId, "attraction", result.Value!.Name, result.Value.Address, result.Value.Lat, result.Value.Lon))
            : result.Forward<TripPlace>();
    }

    /** The fields of the Attractions response that a trip snapshot keeps. */
    private sealed record AttractionResponse(string Name, string Address, decimal Lat, decimal Lon);
}
