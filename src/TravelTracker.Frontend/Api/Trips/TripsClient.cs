namespace TravelTracker.Frontend.Api.Trips;

/**
 * Saves and reads the caller's own trips through the independent Trips service's public HTTP contract.
 * @param client Sends bounded calls with the current session token.
 */
public sealed class TripsClient(ProtectedClient client)
{
    const string Path = "/api/v1/trips";

    /**
     * Saves a complete itinerary in one request; the service stores all of it or none of it.
     * @param request The public create body, without any owner or server identity.
     * @param cancellationToken Cancels the pending save.
     * @return The server-confirmed saved trip.
     */
    public async Task<TripDto> CreateAsync(object request, CancellationToken cancellationToken = default) =>
        await client.SendJsonAsync<TripDto>("Trips", HttpMethod.Post, Path, request, cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The save could not be confirmed. Open your trips before retrying to avoid a duplicate.");

    /**
     * Reads one of the caller's own trips.
     * @param id Public trip identifier.
     * @param cancellationToken Cancels a closed details view.
     * @return The stored trip; another account's trip is reported as absent by the service.
     */
    public async Task<TripDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await client.GetJsonAsync<TripDto>("Trips", $"{Path}/{id:D}", cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The trip response was empty. Try again.");
}
