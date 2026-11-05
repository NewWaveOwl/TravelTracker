using System.Security.Claims;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Data.Repositories;

namespace TravelTracker.Users.Api.Services;

/**
 * Authorizes one-time first-administrator provisioning before invoking durable storage.
 * @param configuration Operator-owned bootstrap target and secret.
 * @param identities Verifies provider email ownership.
 * @param repository Stores service-owned account data and the permanent bootstrap-consumed marker.
 */
public sealed class BootstrapService(IConfiguration configuration, VerifiedIdentityClient identities, IUserRepository repository)
{
    /**
     * Provisions only the configured, verified account. Request body values can never select an identity or role.
     * @param user Principal from real bearer validation.
     * @param secret Operator credential supplied by the setup client.
     * @param token Validated access token for provider userinfo.
     * @param cancellationToken Request cancellation.
     * @return HTTP result without credentials or provider identity details.
     */
    public async Task<IResult> CreateAsync(ClaimsPrincipal user, string? secret, string token, CancellationToken cancellationToken)
    {
        var expectedEmail = configuration["Bootstrap:Email"];
        var expectedIssuer = configuration["Bootstrap:Issuer"];
        if (string.IsNullOrWhiteSpace(expectedEmail) || string.IsNullOrWhiteSpace(expectedIssuer) || configuration["Bootstrap:Secret"] is not { Length: >= 32 })
            return Results.Problem(statusCode: 404, title: "First-administrator setup is unavailable.");
        if (!SecretVerifier.Matches(configuration["Bootstrap:Secret"], secret)) return Results.Forbid();
        var issuer = user.FindFirstValue("iss");
        var subject = user.FindFirstValue("sub");
        if (issuer != expectedIssuer || string.IsNullOrWhiteSpace(subject)) return Results.Forbid();
        var email = await identities.EmailAsync(issuer, subject, token, cancellationToken);
        if (email is null || !string.Equals(email, expectedEmail, StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
        var account = await repository.BootstrapAsync(issuer, subject, email, cancellationToken);
        return account is null
            ? Results.Problem(statusCode: 409, title: "First-administrator setup has already been completed.")
            : Results.Ok(new UserProfileResponse(account.UserId, account.Email, account.Role, account.Status));
    }
}
