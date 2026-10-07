namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains writable fields while retaining missing required values.
 */
public sealed class CreateTransportModeRequest
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}
