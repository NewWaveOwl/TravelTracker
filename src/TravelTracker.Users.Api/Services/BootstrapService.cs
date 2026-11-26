using System.Security.Claims;
using TravelTracker.Security.Authentication;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Data.Repositories.Credentials;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Api.Services;

/**
 * Authorizes one-time first-administrator provisioning before invoking durable storage.
 * @param configuration Operator-owned bootstrap target and secret.
 * @param credentials Finds the account registered under the configured username (Local provider).
 * @param identities Verifies the confirmed email behind the token (Supabase provider).
 * @param repository Stores service-owned account data and the permanent bootstrap-consumed marker.
 * @param logger Records setup decisions without identities, usernames, emails or credentials.
 */
public sealed class BootstrapService(IConfiguration configuration, ICredentialRepository credentials, VerifiedIdentityClient identities,
    IUserRepository repository, ILogger<BootstrapService> logger)
{
    /**
     * Provisions only the configured, verified account. Request body values can never select an identity or role.
     * @param user Principal from real bearer validation.
     * @param secret Operator credential supplied by the setup client.
     * @param token Validated access token, used to confirm the email with Supabase.
     * @param cancellationToken Request cancellation.
     * @return HTTP result without credentials or provider identity details.
     */
    public async Task<IResult> CreateAsync(ClaimsPrincipal user, string? secret, string token, CancellationToken cancellationToken)
    {
        var supabase = configuration["Authentication:Bearer:Provider"] == BearerSettings.SupabaseProvider;
        var target = supabase ? "Bootstrap:Email" : "Bootstrap:Username";
        var expectedTarget = configuration[target];
        var expectedIssuer = configuration["Bootstrap:Issuer"];
        if (string.IsNullOrWhiteSpace(expectedTarget) || string.IsNullOrWhiteSpace(expectedIssuer) || configuration["Bootstrap:Secret"] is not { Length: >= 32 })
        {
            logger.LogWarning("First-administrator setup refused: {Target}, Bootstrap:Issuer or a Bootstrap:Secret of at least 32 characters is not configured.", target);
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "First-administrator setup is unavailable.");
        }
        if (!SecretVerifier.Matches(configuration["Bootstrap:Secret"], secret))
        {
            logger.LogWarning("First-administrator setup refused: the setup secret did not match.");
            return Results.Forbid();
        }
        var issuer = user.FindFirstValue("iss");
        var subject = user.FindFirstValue("sub");
        if (issuer != expectedIssuer || string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("First-administrator setup refused: the token issuer or subject claim is not the configured one.");
            return Results.Forbid();
        }
        if (supabase)
        {
            var email = await identities.EmailAsync(issuer, subject, token, cancellationToken);
            if (email is null || !string.Equals(email, expectedTarget, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("First-administrator setup refused: the verified provider email was missing or not the configured one.");
                return Results.Forbid();
            }
        }
        else
        {
            // the setup secret is the proof of ownership; the token must belong to the account registered under the configured username
            var registered = await credentials.FindAsync(expectedTarget, cancellationToken);
            if (registered is null || registered.Account.ProviderIdentity != new ProviderIdentity(issuer, subject))
            {
                logger.LogWarning("First-administrator setup refused: no account registered under the configured username belongs to this token.");
                return Results.Forbid();
            }
        }
        var account = await repository.BootstrapAsync(issuer, subject, cancellationToken);
        if (account is null)
        {
            logger.LogWarning("First-administrator setup refused: it was already completed for a different account or is no longer possible.");
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "First-administrator setup has already been completed.");
        }
        logger.LogInformation("First-administrator setup confirmed.");
        return Results.Ok(UserProfileResponse.From(account));
    }
}
