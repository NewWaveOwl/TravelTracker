namespace TravelTracker.Frontend.Api.Attractions;

/**
 * Consumes the independent Attractions service through authenticated public HTTP contracts only.
 * @param client Sends bounded calls with the current session token.
 */
public sealed class AttractionsClient(ProtectedClient client)
{
    const string Path = "/api/v1/attractions";

    /**
     * Searches names using one encoded name parameter; server ordering is preserved.
     * @param name Literal name substring, or blank for all visible records.
     * @param cancellationToken Cancels stale searches.
     * @return Ordered visible catalogue records.
     */
    public async Task<IReadOnlyList<AttractionDto>> SearchAsync(string? name, CancellationToken cancellationToken = default) =>
        await client.GetJsonAsync<List<AttractionDto>>("Attractions", string.IsNullOrWhiteSpace(name) ? Path : $"{Path}?name={Uri.EscapeDataString(name.Trim())}", cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The catalogue response was empty. Try again.");

    /**
     * Reads one visible attraction.
     * @param id Public attraction identifier.
     * @param cancellationToken Cancels a closed details view.
     * @return Current attraction details.
     */
    public async Task<AttractionDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await client.GetJsonAsync<AttractionDto>("Attractions", $"{Path}/{id:D}", cancellationToken) ?? throw EmptyResponse();

    /**
     * Creates a private attraction; service policy remains authoritative.
     * @param fields Writable public fields, without server-owned identifiers.
     * @param cancellationToken Cancels the pending save.
     * @return The server-confirmed saved record.
     */
    public async Task<AttractionDto> CreateAsync(Dictionary<string, object?> fields, CancellationToken cancellationToken = default) =>
        await client.SendJsonAsync<AttractionDto>("Attractions", HttpMethod.Post, Path, fields, cancellationToken) ?? throw EmptyResponse();

    /**
     * Saves only edited fields; explicit null clears description or rating.
     * @param id Public attraction identifier.
     * @param fields Changed public fields only.
     * @param cancellationToken Cancels the pending save.
     * @return The server-confirmed saved record.
     */
    public async Task<AttractionDto> UpdateAsync(Guid id, Dictionary<string, object?> fields, CancellationToken cancellationToken = default) =>
        await client.SendJsonAsync<AttractionDto>("Attractions", HttpMethod.Patch, $"{Path}/{id:D}", fields, cancellationToken) ?? throw EmptyResponse();

    static ApiError EmptyResponse() => new(FailureKind.Unexpected, "The save could not be confirmed. Check the catalogue before retrying.");
}
