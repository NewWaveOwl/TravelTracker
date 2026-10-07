using TravelTracker.Transport.Api.Data;
namespace TravelTracker.Transport.Api.Services;
/**
 * Coordinates TransportMode operations implemented in separate feature files.
 * @param repository Scoped resource persistence.
 */
public sealed partial class TransportModeService(ITransportModeRepository repository) : ITransportModeService;
