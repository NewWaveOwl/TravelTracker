using TravelTracker.Api.Trips;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.References;

/**
 * Reads attractions from the public Attractions contract under the caller's own token,
 * so private attractions owned by someone else come back as not found.
 *
 * @param http Typed client whose base address points at the Attractions service.
 * @param sender Sends the request and logs failures.
 */
public sealed class AttractionReferenceClient(HttpClient http, ReferenceSender sender)
{
    /**
     * Reads one attraction the caller may see and copies it into a place snapshot.
     *
     * @param attractionId Public attraction identifier.
     * @param bearerToken The caller's access token, forwarded to the catalogue unchanged.
     * @param cancellationToken Cancels the lookup.
     * @return A place snapshot when found, otherwise the non-found outcome.
     */
    public async Task<ReferenceResult<TripPlace>> GetPlaceAsync(Guid attractionId, string bearerToken, CancellationToken cancellationToken)
    {
        var operation = "attraction lookup";
        var result = await sender.SendAsync<AttractionResponse>(http,
            ReferenceRequest.Get($"api/v1/attractions/{attractionId:D}", bearerToken), operation, cancellationToken);
        if (result.Status != ReferenceStatus.Found) return result.Forward<TripPlace>();
        var place = result.Value!;
        return !ReferenceSender.IsUsablePlace(place.Name, place.Address, place.Lat, place.Lon)
            ? sender.Incomplete<TripPlace>(operation)
            : ReferenceResult<TripPlace>.Found(new(attractionId, TripPlaceTypes.Attraction, place.Name, place.Address, place.Lat.Value, place.Lon.Value));
    }

    /** The fields of the Attractions response that a trip snapshot keeps; a missing one is detected, never defaulted. */
    private sealed record AttractionResponse(string? Name, string? Address, decimal? Lat, decimal? Lon);
}
