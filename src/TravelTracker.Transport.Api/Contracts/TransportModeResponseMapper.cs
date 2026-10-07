using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Contracts;
/**
 * Maps transport entries to their public fields.
 */
public static class TransportModeResponseMapper
{
    /**
     * Maps saved data to the public representation.
     * @param value Saved entry.
     * @return Its public representation.
     */
    public static TransportModeResponse From(TransportModeRecord value) =>
        new(value.TransportModeId, value.Code, value.Name, value.Description);
}
