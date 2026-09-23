using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

public interface IAttractionRepository
{
    Task<List<Attraction>> GetAllAsync();

    Task<Attraction?> GetByIdAsync(Guid attractionId);

    Task AddAsync(Attraction attraction);

    Task UpdateAsync(Attraction attraction);

    Task<bool> DeleteAsync(Guid attractionId);
}
