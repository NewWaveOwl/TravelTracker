using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Api.Access;

/** Contains only validated identity and live Users authorization data for one request. */
public sealed class CallerContext
{
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
    public bool IsBanned { get; set; }

    /**
     * Applies private creator-only and active shared-catalogue visibility.
     * @param accommodation The candidate accommodation record.
     * @return Whether this caller may read the record.
     */
    public bool CanRead(Accommodation accommodation) =>
        !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Subject) &&
        (accommodation.IsShared
            ? !IsBanned
            : accommodation.OwnerIssuer == Issuer && accommodation.OwnerSubject == Subject);
}
