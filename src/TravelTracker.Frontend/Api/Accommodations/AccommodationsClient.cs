namespace TravelTracker.Frontend.Api.Accommodations;

/**
 * Consumes the independent Accommodations service through its authenticated public HTTP contract.
 * @param client Sends bounded calls with the current session token.
 */
public sealed class AccommodationsClient(ProtectedClient client)
{
    const string Path = "/api/v1/accommodations";

    /**
     * Searches the caller-visible catalogue; the service controls ordering and privacy filtering.
     * @param name A literal name substring, or blank to browse all visible records.
     * @param cancellationToken Cancels an obsolete search.
     * @return Visible public accommodation records.
     */
    public async Task<IReadOnlyList<AccommodationDto>> SearchAsync(string? name, CancellationToken cancellationToken = default) =>
        await client.GetJsonAsync<List<AccommodationDto>>("Accommodations", string.IsNullOrWhiteSpace(name) ? Path : $"{Path}?name={Uri.EscapeDataString(name.Trim())}", cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The accommodation response was empty. Try again.");

    /**
     * Reads a detail only when the service says it is visible to the caller.
     * @param id Public accommodation identifier.
     * @param cancellationToken Cancels a closed detail view.
     * @return The current public accommodation details.
     */
    public async Task<AccommodationDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await client.GetJsonAsync<AccommodationDto>("Accommodations", $"{Path}/{id:D}", cancellationToken)
        ?? throw EmptyResponse();

    /**
     * Creates a place without supplying identity or persistence fields.
     * @param request Caller-writable accommodation values.
     * @param cancellationToken Cancels the pending save.
     * @return The server-confirmed saved record.
     */
    public async Task<AccommodationDto> CreateAsync(CreateAccommodationRequest request, CancellationToken cancellationToken = default) =>
        await client.SendJsonAsync<AccommodationDto>("Accommodations", HttpMethod.Post, Path, request, cancellationToken)
        ?? throw EmptyResponse();

    static ApiError EmptyResponse() => new(FailureKind.Unexpected, "The save could not be confirmed. Check the catalogue before retrying.");
}
