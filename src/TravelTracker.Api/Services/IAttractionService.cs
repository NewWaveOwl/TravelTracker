using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

public interface IAttractionService
{
    Task<List<Attraction>> GetAllAsync();

    Task<Attraction?> GetByIdAsync(Guid attractionId);

    Task<Attraction> CreateAsync(Attraction attraction);

    Task<Attraction?> UpdateAsync(
        Guid attractionId,
        Attraction attraction);

    Task<bool> DeleteAsync(Guid attractionId);
}
