using TravelTracker.Transport.Api.Data;
namespace TravelTracker.Transport.Api.Services;
/**
 * Coordinates TransportRoute operations implemented in separate feature files.
 * @param repository Scoped resource persistence.
 * @param transports Transport reference lookup.
 * @param points Location reference lookup.
 */
public sealed partial class TransportRouteService(ITransportRouteRepository repository, ITransportModeRepository transports, IRoutePointRepository points) : ITransportRouteService;
