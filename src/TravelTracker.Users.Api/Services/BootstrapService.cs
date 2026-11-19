using System.Security.Claims;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Data.Repositories.Credentials;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Api.Services;

/**
 * Authorizes one-time first-administrator provisioning before invoking durable storage.
 * @param configuration Operator-owned bootstrap target and secret.
 * @param credentials Finds the account registered under the configured email.
 * @param repository Stores service-owned account data and the permanent bootstrap-consumed marker.
 * @param logger Records setup decisions without identities, emails or credentials.
 */
public sealed class BootstrapService(IConfiguration configuration, ICredentialRepository credentials, IUserRepository repository, ILogger<BootstrapService> logger)
{
    /**
     * Provisions only the configured, verified account. Request body values can never select an identity or role.
     * @param user Principal from real bearer validation.
     * @param secret Operator credential supplied by the setup client.
     * @param cancellationToken Request cancellation.
     * @return HTTP result without credentials or provider identity details.
     */
    public async Task<IResult> CreateAsync(ClaimsPrincipal user, string? secret, CancellationToken cancellationToken)
    {
        var expectedEmail = configuration["Bootstrap:Email"];
        var expectedIssuer = configuration["Bootstrap:Issuer"];
        if (string.IsNullOrWhiteSpace(expectedEmail) || string.IsNullOrWhiteSpace(expectedIssuer) || configuration["Bootstrap:Secret"] is not { Length: >= 32 })
        {
            logger.LogWarning("First-administrator setup refused: Bootstrap:Email, Bootstrap:Issuer or a Bootstrap:Secret of at least 32 characters is not configured.");
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
        // the setup secret is the proof of ownership; the token must belong to the account registered under the configured email
        var registered = await credentials.FindAsync(expectedEmail, cancellationToken);
        if (registered is null || registered.Account.ProviderIdentity != new ProviderIdentity(issuer, subject))
        {
            logger.LogWarning("First-administrator setup refused: no account registered under the configured email belongs to this token.");
            return Results.Forbid();
        }
        var account = await repository.BootstrapAsync(issuer, subject, expectedEmail, cancellationToken);
        if (account is null)
        {
            logger.LogWarning("First-administrator setup refused: it was already completed for a different account or is no longer possible.");
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "First-administrator setup has already been completed.");
        }
        logger.LogInformation("First-administrator setup confirmed.");
        return Results.Ok(UserProfileResponse.From(account));
    }
}
