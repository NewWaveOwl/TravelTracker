using System.Security.Claims;

namespace TravelTracker.Api.Authentication;

/**
 * Names the separate permissions used by the representative catalogue API.
 */
public static class ScopePolicies
{
    public const string CatalogueRead = "catalogue.read";
    public const string CatalogueWrite = "catalogue.write";
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";

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
