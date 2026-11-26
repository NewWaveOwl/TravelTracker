namespace TravelTracker.Frontend.Api.Trips;

/**
 * Exchanges the public Trips contract with the service.
 * @param client Sends requests with the current session token.
 */
public sealed class TripsClient(ProtectedClient client)
{
    sealed record StatusResponse(string? Status);

    public async Task<string?> SetStatusAsync(Guid tripId, string status, CancellationToken token)
    {
        var result = await client.SendJsonAsync<StatusResponse>("Trips", HttpMethod.Put,
            $"{Path}/{tripId:D}/status", new { status }, token);
        if (result is null || result.Status != (status == "automatic" ? null : status))
            throw new ApiError(FailureKind.Unexpected, "The trip status update could not be confirmed.");
        return result.Status;
    }

    const string Path = "/api/v1/trips";

    /**
     * Lists the trips saved by the current user.
     * @param token Cancels a request when History closes or the session changes.
     * @return The user's saved trip summaries.
     */
    public async Task<IReadOnlyList<TripSummaryDto>> ListAsync(CancellationToken token) =>
        await client.GetJsonAsync<TripSummaryDto[]>("Trips", Path, token)
        ?? throw new ApiError(FailureKind.Unexpected, "The Trips service returned no history.");

    /**
     * Loads an existing trip and its stored snapshots.
     * @param identifier Public trip identifier.
     * @param cancellationToken Cancels a closed editor.
     * @return The current user's saved trip.
     */
    public async Task<TripDto> GetAsync(Guid identifier, CancellationToken cancellationToken) =>
        await client.GetJsonAsync<TripDto>("Trips", $"{Path}/{identifier:D}", cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The Trips service returned no trip.");

    /**
     * Loads the saved itinerary and the entity tag needed to replace it.
     * @param identifier Public trip identifier.
     * @param cancellationToken Cancels a closed editor.
     * @return The current trip and its entity tag.
     */
    public async Task<TripRevision> GetRevisionAsync(Guid identifier, CancellationToken cancellationToken) =>
        ReadRevision(await client.SendAsync<TripDto>("Trips", HttpMethod.Get, $"{Path}/{identifier:D}", null, null, cancellationToken));

    /**
     * Creates a trip or saves edits to the same trip.
     * @param identifier Public identifier for edits, or null for a new trip.
     * @param request Validated trip details and catalogue references.
     * @param etag Entity tag from the last read or save for an existing trip.
     * @param cancellationToken Cancels a closed editor.
     * @return The trip confirmed by the service.
     * @throws ApiError With status 428 and no request sent when an existing trip has no entity tag; the status mirrors the service's own precondition-required code.
     */
    public async Task<TripRevision> SaveAsync(Guid? identifier, SaveTripRequest request, string? etag, CancellationToken cancellationToken)
    {
        if (identifier is not null && etag is null)
            throw new ApiError(FailureKind.Unexpected, "Reopen the saved trip before saving changes.", 428);
        return ReadRevision(await client.SendAsync<TripDto>("Trips", identifier is null ? HttpMethod.Post : HttpMethod.Put,
            identifier is null ? Path : $"{Path}/{identifier:D}", request, etag, cancellationToken));
    }

    /**
     * Creates a trip from the form planner.
     * @param request Validated trip details and catalogue references.
     * @param cancellationToken Cancels a closed editor.
     * @return The trip confirmed by the service.
     */
    public async Task<TripDto> CreateAsync(SaveTripRequest request, CancellationToken cancellationToken) =>
        await client.SendJsonAsync<TripDto>("Trips", HttpMethod.Post, Path, request, cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The trip save could not be confirmed.");

    static TripRevision ReadRevision(ServiceResponse<TripDto> response) =>
        response is { Body: { } trip, ETag: { } etag } ? new(trip, etag)
        : throw new ApiError(FailureKind.Unexpected, "The Trips service returned no confirmed version. Reopen the trip to check the latest version.");
}
