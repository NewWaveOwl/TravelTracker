using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Api.Services;

/** defines the operations on the caller's own preferences. */
public interface ISettingsService
{
    /**
     * reads the caller's saved preferences, or the documented defaults (revision 0) when none were saved.
     * @param cancellationToken cancels database I/O.
     * @return the caller's current preferences.
     */
    Task<UserSettings> GetAsync(CancellationToken cancellationToken = default);

    /**
     * validates the candidate, then replaces the caller's preferences only if the stored revision still matches.
     * nothing is saved when validation fails or the revision is stale.
     * @param candidate the complete submitted values.
     * @param expectedRevision the revision the caller read, taken from If-Match.
     * @param cancellationToken cancels database I/O.
     * @return the saved values, field errors, or a stale marker.
     */
    Task<SettingsReplaceResult> ReplaceAsync(UserSettings candidate, long expectedRevision, CancellationToken cancellationToken = default);
}
