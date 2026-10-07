using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Contracts;
/**
 * Maps location snapshots to their public coordinate names.
 */
public static class RoutePointResponseMapper
{
    /**
     * Maps saved data to the public representation.
     * @param value Saved location.
     * @return Its public representation.
     */
    public static RoutePointResponse From(RoutePointRecord value) => new(
        value.RoutePointId, value.Type, value.Name, value.Address,
        value.Latitude, value.Longitude, value.SourceId);
}
