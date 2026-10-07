using TravelTracker.Attractions.Api.Contracts;
using TravelTracker.Attractions.Api.Models;
using TravelTracker.Attractions.Api.Validation;
namespace TravelTracker.Attractions.Api.Services;
public sealed partial class AttractionService
{
    /**
     * Validates before generating and saving a server-owned identifier.
     * @param request Writable create fields.
     * @param cancellationToken Cancels the request.
     * @return The saved attraction.
     */
    public async Task<AttractionRecord> CreateAsync(CreateAttractionRequest request, CancellationToken cancellationToken)
    {
        AttractionValidator.Validate(request);
        var value = new AttractionRecord(Guid.NewGuid(), request.Name!.Trim(), request.Description,
            request.Rating, request.Address!, request.Lat!.Value, request.Lon!.Value,
            request.EstimatedVisitMinutes!.Value);
        await repository.AddAsync(value, cancellationToken);
        return value;
    }
}
