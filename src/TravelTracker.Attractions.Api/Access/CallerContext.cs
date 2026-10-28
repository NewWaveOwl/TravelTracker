using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Access;

/**

 * Contains only server-validated identity and live account permissions for one request.

 */
public sealed class CallerContext
{
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public bool IsAdministrator { get; set; }
    public bool IsBanned { get; set; }

    /**
     * Applies creator-only private visibility, including for administrators.
     * @param attraction The candidate record.
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
