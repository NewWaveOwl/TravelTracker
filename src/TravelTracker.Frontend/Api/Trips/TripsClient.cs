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
     * Loads the saved itinerary and the entity tag needed to replace it.
     * @param id Public trip identifier.
     * @param token Cancels a closed editor.
     * @return The current trip and its entity tag.
     */
    public async Task<TripRevision> GetRevisionAsync(Guid id, CancellationToken token) =>
        ReadRevision(await client.SendAsync<TripDto>("Trips", HttpMethod.Get, $"{Path}/{id:D}", null, null, token));

    /**
     * Creates a trip or saves edits to the same trip.
     * @param id Public identifier for edits, or null for a new trip.
     * @param request Validated trip details and catalogue references.
     * @param etag Entity tag from the last read or save for an existing trip.
     * @param token Cancels a closed editor.
     * @return The trip confirmed by the service.
     */
    public async Task<TripRevision> SaveAsync(Guid? id, SaveTripRequest request, string? etag, CancellationToken token)
    {
        if (id is not null && etag is null)
            throw new ApiError(FailureKind.Unexpected, "Reopen the saved trip before saving changes.", 428);
        return ReadRevision(await client.SendAsync<TripDto>("Trips", id is null ? HttpMethod.Post : HttpMethod.Put,
            id is null ? Path : $"{Path}/{id:D}", request, etag, token));
    }

    /**
     * Creates a trip from the form planner.
     * @param request Validated trip details and catalogue references.
     * @param token Cancels a closed editor.
     * @return The trip confirmed by the service.
     */
    public async Task<TripDto> CreateAsync(SaveTripRequest request, CancellationToken token) =>
        await client.SendJsonAsync<TripDto>("Trips", HttpMethod.Post, Path, request, token)
        ?? throw new ApiError(FailureKind.Unexpected, "The trip save could not be confirmed.");

    static TripRevision ReadRevision(ServiceResponse<TripDto> response) =>
        response is { Body: { } trip, ETag: { } etag } ? new(trip, etag)
        : throw new ApiError(FailureKind.Unexpected, "The Trips service returned no confirmed version. Reopen the trip to check the latest version.");
}
