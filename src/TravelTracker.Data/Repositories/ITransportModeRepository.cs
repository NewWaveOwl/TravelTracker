using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

public interface ITransportModeRepository
{
    Task<List<TransportMode>> GetAllAsync();

    Task<TransportMode?> GetByIdAsync(Guid transportModeId);

    Task<TransportMode?> GetByCodeAsync(string code);

    Task AddAsync(TransportMode transportMode);

    Task UpdateAsync(TransportMode transportMode);
}
