namespace TravelTracker.Frontend.Api.Trips;

/**
 * Exchanges the public Trips contract with the service.
 * @param client Sends requests with the current session token.
 */
public sealed class TripsClient(ProtectedClient client)
{
    const string Path = "/api/v1/trips";

    /**
     * Loads an existing trip and its stored snapshots.
     * @param id Public trip identifier.
     * @param token Cancels a closed editor.
     * @return The current user's saved trip.
     */
    public async Task<TripDto> GetAsync(Guid id, CancellationToken token) =>
        await client.GetJsonAsync<TripDto>("Trips", $"{Path}/{id:D}", token)
        ?? throw new ApiError(FailureKind.Unexpected, "The Trips service returned no trip.");

    /**
     * Creates a trip or saves edits to the same trip.
     * @param id Public identifier for edits, or null for a new trip.
     * @param request Validated trip details and catalogue references.
     * @param token Cancels a closed editor.
     * @return The trip confirmed by the service.
     */
    public async Task<TripDto> SaveAsync(Guid? id, SaveTripRequest request, CancellationToken token) =>
        await client.SendJsonAsync<TripDto>("Trips", id is null ? HttpMethod.Post : HttpMethod.Put,
            id is null ? Path : $"{Path}/{id:D}", request, token)
        ?? throw new ApiError(FailureKind.Unexpected, "The trip save could not be confirmed.");
}
