using TravelTracker.Transport.Api.Data;
namespace TravelTracker.Transport.Api.Services;
/**
 * Coordinates RoutePoint operations implemented in separate feature files.
 * @param repository Scoped resource persistence.
 */
public sealed partial class RoutePointService(IRoutePointRepository repository) : IRoutePointService;
