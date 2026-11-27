namespace TravelTracker.Frontend.Api.Trips;

/** Publishes a saved itinerary place through its owning catalogue service. */
public sealed class TripPlaceSharingClient(ProtectedClient client)
{
    sealed record SharingResult(string Visibility);

    public async Task ShareAsync(TripPlaceDto place, CancellationToken token)
    {
        var (service, resource) = place.Type switch
        {
            "accommodation" => ("Accommodations", "accommodations"),
            "attraction" => ("Attractions", "attractions"),
            "transport_stop" => ("Transport", "route-points"),
            _ => throw new ApiError(FailureKind.Validation, "This type of stop cannot be shared.")
        };
        var result = await client.SendJsonAsync<SharingResult>(service, HttpMethod.Post,
            $"/api/v1/{resource}/{place.PlaceId:D}/share", null, token);
        if (result?.Visibility != "shared")
            throw new ApiError(FailureKind.Unexpected, "The catalogue did not confirm this place was shared.");
    }
}
