using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

public interface ITransportModeService
{
    Task<List<TransportMode>> GetAllAsync();

    Task<TransportMode?> GetByIdAsync(Guid transportModeId);

    Task<TransportMode?> GetByCodeAsync(string code);

    Task<TransportMode> CreateAsync(TransportMode transportMode);

    Task<TransportMode?> UpdateAsync(
        Guid transportModeId,
        TransportMode transportMode);
}
