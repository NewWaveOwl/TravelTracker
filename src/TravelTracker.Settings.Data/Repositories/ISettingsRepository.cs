using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Data.Repositories;

/** defines persistence operations for one account's preferences. */
public interface ISettingsRepository
{
    /**
     * loads the saved preferences for one validated identity.
     * @param issuer the validated token issuer.
     * @param subject the validated token subject.
     * @param cancellationToken cancels database I/O.
     * @return detached saved values with their revision, or null when the account has never saved preferences.
     */
    Task<UserSettings?> GetByOwnerAsync(string issuer, string subject, CancellationToken cancellationToken = default);

    /**
     * replaces the saved preferences only when the stored revision still equals the expected one.
     * the check and the write happen in one statement, so two racing saves cannot both succeed.
     * an expected revision of 0 means the caller saw no saved row and inserts the first one.
     * @param issuer the validated token issuer.
     * @param subject the validated token subject.
     * @param settings the complete accepted values to store.
     * @param expectedRevision the revision the caller last read.
     * @param cancellationToken cancels database I/O.
     * @return the stored values with their new revision, or null when the revision was stale.
     */
    Task<UserSettings?> ReplaceIfRevisionAsync(string issuer, string subject, UserSettings settings, long expectedRevision,
        CancellationToken cancellationToken = default);
}
