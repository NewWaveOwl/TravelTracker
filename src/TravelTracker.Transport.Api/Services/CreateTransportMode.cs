using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Validation;
namespace TravelTracker.Transport.Api.Services;
public sealed partial class TransportModeService
{
    /**
     * Validates and saves a new record.
     * @param request Mode, name and description without a schedule.
     * @param cancellationToken Cancels the request.
     * @return The saved catalogue entry.
     */
    public async Task<TransportModeRecord> CreateAsync(CreateTransportModeRequest request, CancellationToken cancellationToken)
    {
        var code = TransportModeValidator.Validate(request);
        var value = new TransportModeRecord(Guid.NewGuid(), code, request.Name!.Trim(), request.Description);
        await repository.AddAsync(value, cancellationToken);
        return value;
    }
}
