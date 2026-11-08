using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.References;

/**
 * Reads transport modes from the public Transport contract by their public identifier.
 *
 * @param http Typed client whose base address points at the Transport service.
 * @param sender Sends the request and logs failures.
 */
public sealed class TransportModeReferenceClient(HttpClient http, ReferenceSender sender) {
    /**
     * Reads one transport mode and copies it into a snapshot.
     *
     * @param transportModeId Public transport mode identifier.
     * @param bearerToken The caller's access token, forwarded to the catalogue unchanged.
     * @param cancellationToken Cancels the lookup.
     * @return A transport snapshot when found, otherwise the non-found outcome.
     */
    public async Task<ReferenceResult<TripTransport>> GetModeAsync(Guid transportModeId, string bearerToken, CancellationToken cancellationToken) {
        var operation = $"transport mode {transportModeId:D} lookup";
        var result = await sender.SendAsync<TransportModeResponse>(http,
            ReferenceRequest.Get($"api/v1/transport-modes/{transportModeId:D}", bearerToken), operation, cancellationToken);
        if (result.Status != ReferenceStatus.Found) return result.Forward<TripTransport>();
        var mode = result.Value!;
        return string.IsNullOrWhiteSpace(mode.Code) || string.IsNullOrWhiteSpace(mode.Name)
            ? sender.Incomplete<TripTransport>(operation)
            : ReferenceResult<TripTransport>.Found(new(transportModeId, mode.Code, mode.Name));
    }

    /** The fields of the Transport mode response that a trip snapshot keeps. */
    private sealed record TransportModeResponse(string? Code, string? Name);
}
