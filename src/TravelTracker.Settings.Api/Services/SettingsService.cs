using Microsoft.Extensions.Logging;
using TravelTracker.Settings.Api.Access;
using TravelTracker.Settings.Data.Repositories;
using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Api.Services;

/**
 * applies preference validation, revision checks and per-account ownership over the owned store.
 * @param repository the scoped PostgreSQL repository boundary.
 * @param caller the validated identity for this request.
 * @param logger safe diagnostics for refused saves; never logs identities or values.
 */
public sealed class SettingsService(ISettingsRepository repository, CallerContext caller, ILogger<SettingsService> logger) : ISettingsService
{
    /**
     * reads the caller's saved preferences, falling back to defaults without writing anything.
     * @param cancellationToken cancels database I/O.
     * @return the caller's current preferences.
     * @throws UnauthorizedAccessException when no validated identity reached the service; the host maps it to 401.
     */
    public async Task<UserSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        RequireIdentity();
        return await repository.GetByOwnerAsync(caller.Issuer, caller.Subject, cancellationToken) ?? new UserSettings();
    }

    /**
     * validates the candidate first, then replaces the caller's row only if its revision still equals the expected one.
     * @param candidate the complete submitted values.
     * @param expectedRevision the revision the caller read, taken from If-Match; null when the header is malformed and so matches nothing.
     * @param cancellationToken cancels database I/O.
     * @return the saved values, field errors, or a stale marker.
     * @throws UnauthorizedAccessException when no validated identity reached the service.
     */
    public async Task<SettingsReplaceResult> ReplaceAsync(UserSettings candidate, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        RequireIdentity();
        var errors = candidate.Validate();
        if (errors.Count != 0)
        {
            logger.LogInformation("Settings replace rejected: {ErrorCount} field(s) failed validation.", errors.Count);
            return new(null, errors);
        }
        var saved = expectedRevision is { } revision
            ? await repository.ReplaceIfRevisionAsync(caller.Issuer, caller.Subject, candidate, revision, cancellationToken)
            : null;
        if (saved is null)
        {
            logger.LogWarning("Settings replace refused: the If-Match revision is stale or does not match the stored settings.");
            return new(null, null, Stale: true);
        }
        logger.LogInformation("Settings saved at revision {Revision}.", saved.Revision);
        return new(saved, null);
    }

    /**
     * refuses work for requests without a validated issuer and subject.
     * @throws UnauthorizedAccessException when either claim is missing; the host maps it to 401.
     */
    private void RequireIdentity()
    {
        if (string.IsNullOrWhiteSpace(caller.Issuer) || string.IsNullOrWhiteSpace(caller.Subject))
            throw new UnauthorizedAccessException("A validated caller identity is required.");
    }
}
