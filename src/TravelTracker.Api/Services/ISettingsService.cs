using TravelTracker.Domain.Models;
using TravelTracker.Domain.Types;

namespace TravelTracker.Api.Services;

/**
 * Defines the business operations available for user settings.
 */
public interface ISettingsService
{
    /**
     * Retrieves settings for a user, creating default settings when none exist.
     *
     * @param userId The unique identifier of the user whose settings to retrieve.
     * @return A task containing the user's settings.
     */
    Task<UserSettings> GetOrCreateAsync(Id64 userId);

    /**
     * Validates and updates a user's settings, including preferred transport
     * mode and default map latitude, longitude, and zoom.
     *
     * @param userId The unique identifier of the user whose settings to update.
     * @param settings The new settings values.
     * @return A task containing the updated settings.
     */
    Task<UserSettings> UpdateAsync(
        Id64 userId,
        UserSettings settings);
}
