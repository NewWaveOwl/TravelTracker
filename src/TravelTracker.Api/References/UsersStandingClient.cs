using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTracker.Api.Trips;
using TravelTracker.Security.Authentication;

namespace TravelTracker.Api.References;

/**
 * Checks account standing live through the Users internal contract. The caller's validated
 * issuer and subject resolve to a public account, which is then read with a backend credential
 * that carries users.access. The caller's own token is never sent to Users.
 *
 * @param http Typed client whose base address points at the Users service.
 * @param sender Sends the requests and logs transport and status failures.
 * @param options Supplies the backend Users credential.
 * @param logger Records answers that are present but outside the Users contract.
 * @param tokenProvider Optional auto-refreshing token provider for Supabase service credentials.
 */
public sealed class UsersStandingClient(HttpClient http, ReferenceSender sender, IOptions<TripReferenceOptions> options, ILogger<UsersStandingClient> logger, IServiceAccessTokenProvider? tokenProvider = null)
{
    /**
     * Resolves the caller and reads the account's current standing, without caching.
     *
     * @param caller The issuer and subject validated by the caller's bearer token.
     * @param cancellationToken Cancels the lookups.
     * @return The live standing, NotFound when no account matches the sign-in, or Unavailable when standing cannot be confirmed.
     */
    public async Task<ReferenceResult<AccountStanding>> GetStandingAsync(CallerIdentity caller, CancellationToken cancellationToken)
    {
        var credential = tokenProvider is not null
            ? await tokenProvider.GetTokenAsync(cancellationToken)
            : options.Value.UsersServiceAccessToken;
        if (string.IsNullOrWhiteSpace(credential))
        {
            logger.LogError("Users standing check cannot run: TripReferences:UsersServiceAccessToken is not configured.");
            return ReferenceResult<AccountStanding>.Failed(ReferenceStatus.Unavailable, "the backend Users credential is not configured.");
        }

        var identity = await sender.SendAsync<ResolvedIdentity>(http,
            ReferenceRequest.Post("internal/users/identity", credential, new IdentityRequest(caller.Issuer, caller.Subject)), "users identity lookup", cancellationToken);
        if (identity.Status != ReferenceStatus.Found) return identity.Forward<AccountStanding>();
        var userId = identity.Value!.UserId;
        if (userId == Guid.Empty)
        {
            logger.LogWarning("users identity lookup returned an empty account id; treated as unavailable.");
            return ReferenceResult<AccountStanding>.Failed(ReferenceStatus.Unavailable, "identity lookup returned no account id.");
        }

        var access = await sender.SendAsync<AccessResponse>(http,
            ReferenceRequest.Get($"internal/users/{userId:D}/access", credential), "users access lookup", cancellationToken);
        if (access.Status != ReferenceStatus.Found) return access.Forward<AccountStanding>();
        var standing = access.Value!;
        if (standing.UserId != userId)
        {
            logger.LogWarning("users access lookup answered for a different account; treated as unavailable.");
            return ReferenceResult<AccountStanding>.Failed(ReferenceStatus.Unavailable, "account standing answered for a different account.");
        }
        var recognised = (standing.Status is AccountStanding.Active or AccountStanding.Banned or AccountStanding.Deactivated)
            && (standing.Role is AccountStanding.Traveller or AccountStanding.Administrator);
        if (!recognised)
        {
            logger.LogWarning("users access lookup returned a status or role outside the contract; treated as unavailable.");
            return ReferenceResult<AccountStanding>.Failed(ReferenceStatus.Unavailable, "account standing response was not recognised.");
        }
        return ReferenceResult<AccountStanding>.Found(new(userId, standing.Status, standing.Role));
    }

    /** Body sent to the Users identity lookup. */
    private sealed record IdentityRequest(string Issuer, string Subject);

    /** Users identity lookup response. */
    private sealed record ResolvedIdentity(Guid UserId);

    /** Users access response. */
    private sealed record AccessResponse(Guid UserId, string Status, string Role);
}
