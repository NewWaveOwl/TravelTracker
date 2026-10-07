using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Validation;
namespace TravelTracker.Transport.Api.Services;
public sealed partial class RoutePointService
{
    /**
     * Validates and saves a new record.
     * @param request Typed location snapshot; sourceId is optional metadata.
     * @param cancellationToken Cancels the request.
     * @return The saved location.
     */
    public async Task<RoutePointRecord> CreateAsync(CreateRoutePointRequest request, CancellationToken cancellationToken)
    {
        var type = RoutePointValidator.Validate(request);
        var value = new RoutePointRecord(Guid.NewGuid(), type, request.Name!.Trim(), request.Address!,
            request.Lat!.Value, request.Lon!.Value, request.SourceId);
        await repository.AddAsync(value, cancellationToken);
        return value;
    }
}
