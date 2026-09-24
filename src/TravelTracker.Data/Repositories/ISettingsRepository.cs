using TravelTracker.Domain.Models;
using TravelTracker.Domain.Types;

namespace TravelTracker.Data.Repositories;

/**
 * Defines persistence operations for user settings.
 */
public interface ISettingsRepository
{
    /**
     * Retrieves settings for a user.
     *
     * @param userId The unique identifier of the user whose settings to retrieve.
     * @return A task containing the user's settings, or null when none exist.
     */
    Task<UserSettings?> GetByUserIdAsync(Id64 userId);

    /**
     * Creates or overwrites the settings for a user.
     *
     * @param settings The settings to store.
     * @return A task that represents the asynchronous operation.
     */
    Task UpsertAsync(UserSettings settings);
}
