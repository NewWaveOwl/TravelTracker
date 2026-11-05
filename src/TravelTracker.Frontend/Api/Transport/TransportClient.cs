namespace TravelTracker.Frontend.Api.Transport;

/**
 * Exchanges saved transport and location contracts through the protected HTTP boundary.
 * @param client Supplies the validated session token and configured service destination.
 */
public sealed class TransportClient(ProtectedClient client)
{
    /**
     * Searches saved entries without inferring journeys or times.
     * @param name Literal name substring.
     * @param code Supported mode filter, or blank for all modes.
     * @param token Cancels the view's pending request.
     * @return Saved entries in service order.
     */
    public async Task<IReadOnlyList<TransportDto>> SearchTransportAsync(string name, string code, CancellationToken token = default) =>
        await client.GetJsonAsync<List<TransportDto>>("Transport", Query("transport-modes", name, "code", code), token) ?? throw Empty();

    /**
     * Searches reusable location records.
     * @param name Literal name substring.
     * @param type Supported location type, or blank for all types.
     * @param token Cancels the pending request.
     * @return Saved locations in service order.
     */
    public async Task<IReadOnlyList<RoutePointDto>> SearchPointsAsync(string name, string type, CancellationToken token = default) =>
        await client.GetJsonAsync<List<RoutePointDto>>("Transport", Query("route-points", name, "type", type), token) ?? throw Empty();

    /**
     * Reads a saved record by its public identifier.
     * @param id Saved entry public identifier.
     * @param token Cancels the pending request.
     * @return Current saved details.
     */
    public async Task<TransportDto> GetTransportAsync(Guid id, CancellationToken token = default) =>
        await client.GetJsonAsync<TransportDto>("Transport", $"/api/v1/transport-modes/{id:D}", token) ?? throw Empty();

    /**
     * Reads a saved location by its public identifier.
     * @param id Saved point public identifier.
     * @param token Cancels the pending request.
     * @return Current saved details.
     */
    public async Task<RoutePointDto> GetPointAsync(Guid id, CancellationToken token = default) =>
        await client.GetJsonAsync<RoutePointDto>("Transport", $"/api/v1/route-points/{id:D}", token) ?? throw Empty();

    /**
     * Creates an authorized catalogue entry using public writable fields only.
     * @param fields Validated form fields; no server identity is sent.
     * @param token Cancels the pending request.
     * @return Server-confirmed saved entry.
     */
    public async Task<TransportDto> CreateTransportAsync(Dictionary<string, object?> fields, CancellationToken token = default) =>
        await client.SendJsonAsync<TransportDto>("Transport", HttpMethod.Post, "/api/v1/transport-modes", fields, token) ?? throw Empty();

    /**
     * Creates an authorized reusable location before it can enter a selection.
     * @param fields Validated form fields; no server identity is sent.
     * @param token Cancels the pending request.
     * @return Server-confirmed saved location.
     */
    public async Task<RoutePointDto> CreatePointAsync(Dictionary<string, object?> fields, CancellationToken token = default) =>
        await client.SendJsonAsync<RoutePointDto>("Transport", HttpMethod.Post, "/api/v1/route-points", fields, token) ?? throw Empty();

    static string Query(string resource, string name, string key, string value) =>
        $"/api/v1/{resource}?name={Uri.EscapeDataString(name.Trim())}&{key}={Uri.EscapeDataString(value)}";

    static ApiError Empty() => new(FailureKind.Unexpected, "The Transport service returned no record. Refresh before retrying.");
}
