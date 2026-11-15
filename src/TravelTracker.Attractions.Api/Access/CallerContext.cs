using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Access;

/**
 * Contains only server-validated identity and live account permissions for one request.
 */
public sealed class CallerContext
{
    /** Gets or sets the validated token issuer. */
    public string Issuer { get; set; } = "";

    /** Gets or sets the validated token subject. */
    public string Subject { get; set; } = "";

    /** Gets or sets whether live Users data marks the caller as an administrator. */
    public bool IsAdministrator { get; set; }

    /** Gets or sets whether live Users data marks the caller as banned. */
    public bool IsBanned { get; set; }

    /**
     * Applies creator-only private visibility, including for administrators.
     * @param attraction The stored record to check.
     * @return Whether this caller can see the record.
     */
    public bool CanRead(Attraction attraction) => !string.IsNullOrEmpty(Issuer) && !string.IsNullOrEmpty(Subject) &&
        (attraction.IsShared ? !IsBanned : attraction.OwnerIssuer == Issuer && attraction.OwnerSubject == Subject);

    /**
     * Permits private owners or shared-catalogue administrators to write.
     * @param attraction The visible record.
     * @return Whether editing is permitted.
     */
    public bool CanWrite(Attraction attraction) => !IsBanned && CanRead(attraction) && (!attraction.IsShared || IsAdministrator);
}
