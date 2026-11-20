using System.Security.Claims;

namespace TravelTracker.Security.Authentication;

/**
 * Names the separate permissions used by the catalogue, Settings, Trips, Users, Transport and Attractions APIs.
 */
public static class ScopePolicies
{
    /** Reads accommodations, attractions and transport catalogue data. */
    public const string CatalogueRead = "catalogue.read";

    /** Writes accommodations, attractions and transport catalogue data. */
    public const string CatalogueWrite = "catalogue.write";

    /** Reads the caller's own Settings. */
    public const string SettingsRead = "settings.read";

    /** Replaces the caller's own Settings. */
    public const string SettingsWrite = "settings.write";

    /** Reads the caller's own trips. */
    public const string TripsRead = "trips.read";

    /** Creates and replaces the caller's own trips. */
    public const string TripsWrite = "trips.write";
    public const string UsersAdmin = "users.admin";

    /** Reads Users account profiles through the public Users contract. */
    public const string UsersRead = "users.read";

    /** Updates the caller's own Users profile. */
    public const string UsersWrite = "users.write";

    /** Reads live account standing through the internal Users access contract. */
    public const string UsersAccess = "users.access";

    /** Lists accounts, changes roles and performs first-administrator bootstrap in the Users service. */
    public const string UsersAdministration = "users.admin";

    /**
     * Matches exact, case-sensitive OAuth scopes in space-separated or repeated claims.
     * @param user The validated caller.
     * @param claimType The deployment-selected scope claim name.
     * @param scope The required permission.
     * @return Whether the caller has the exact permission.
     */
    public static bool HasScope(ClaimsPrincipal user, string claimType, string scope) =>
        user.FindAll(claimType).Any(claim =>
            claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal));
}
