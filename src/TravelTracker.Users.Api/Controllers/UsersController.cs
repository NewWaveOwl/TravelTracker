using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TravelTracker.Api.Authentication;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Data.Repositories;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Api.Controllers;

/**
 * Exposes public account contracts, a one-time bootstrap action and a credential-protected service lookup.
 * @param repository Current service-owned account store.
 * @param bootstrap Verifies and performs first-admin setup.
 * @param configuration Operator-owned service credential.
 */
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(IUserRepository repository, BootstrapService bootstrap, VerifiedIdentityClient identities, IConfiguration configuration) : ControllerBase
{
    /**
     * Reads the signed-in account, provisioning only an ordinary traveller after provider email verification.
     * @return Public profile, or a safe identity/standing failure.
     */
    [HttpGet("me")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public async Task<IResult> Me()
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: 400, title: "Account access requires HTTPS.");
        var issuer = User.FindFirstValue("iss"); var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
        var account = await repository.FindAsync(issuer, subject, HttpContext.RequestAborted);
        if (account is null)
        {
            var email = await identities.EmailAsync(issuer, subject, AccessToken(), HttpContext.RequestAborted);
            if (email is null) return Results.Forbid();
            account = await repository.GetOrCreateTravellerAsync(issuer, subject, email, HttpContext.RequestAborted);
        }
        if (account?.Status == UserStatus.Deactivated) return Results.Forbid();
        return account is null ? Results.NotFound() : Results.Ok(new UserProfileResponse(
            account.PublicId,
            account.Email,
            account.Role == UserRole.User ? "traveller" : "administrator",
            account.Status.ToString().ToLowerInvariant()));
    }

    /**
     * Lists registered application accounts only after live administrator authorization in the Users store.
     * @return Public account profiles, or 403 without account data for an inactive/non-administrator caller.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public async Task<IResult> List()
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: 400, title: "Account administration requires HTTPS.");
        var issuer = User.FindFirstValue("iss"); var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
        var accounts = await repository.ListForAdministratorAsync(issuer, subject, HttpContext.RequestAborted);
        return accounts is null ? Results.Forbid() : Results.Ok(accounts.Select(account =>
            new UserProfileResponse(
                account.PublicId,
                account.Email,
                account.Role == UserRole.User ? "traveller" : "administrator",
                account.Status.ToString().ToLowerInvariant())).ToList());
    }

    /**
     * Changes a registered account role with live administrator checks, optimistic role comparison and durable audit.
     * @param id Public target account identifier.
     * @param request Whitelisted desired/current roles and a required written reason.
     * @return Updated public profile, or safe validation, permission, absence or conflict feedback.
     */
    [HttpPatch("{id:guid}/role")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public async Task<IResult> ChangeRole(Guid id, ChangeUserRoleRequest request)
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: 400, title: "Account administration requires HTTPS.");
        var issuer = User.FindFirstValue("iss"); var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Enter a reason for the role change."] });
        var result = await repository.ChangeRoleAsync(issuer, subject, id, request.Role, request.ExpectedRole, request.Reason, HttpContext.RequestAborted);
        return result.Code switch
        {
            "changed" when result.Account is { } account => Results.Ok(new UserProfileResponse(
                account.PublicId,
                account.Email,
                account.Role == UserRole.User ? "traveller" : "administrator",
                account.Status.ToString().ToLowerInvariant())),
            "forbidden" => Results.Forbid(),
            "notfound" => Results.NotFound(),
            "conflict" => Results.Problem(statusCode: 409, title: "The account role changed. Refresh the user list before trying again."),
            "last-administrator" => Results.Problem(statusCode: 409, title: "The last active administrator cannot be demoted."),
            "invalid" => Results.Problem(statusCode: 400, title: "The requested role change is not valid for this account."),
            _ => Results.Problem(statusCode: 503, title: "The role change could not be confirmed.")
        };
    }

    /**
     * Assigns the initial administrator only with both a valid user identity and the setup operator credential.
     * @return Confirmed profile or a safe authentication, authorization or initialization failure.
     */
    [HttpPost("bootstrap-first-administrator")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public Task<IResult> Bootstrap()
    {
        if (!Request.IsHttps) return Task.FromResult<IResult>(Results.Problem(statusCode: 400, title: "Administrator setup requires HTTPS."));
        if (Request.Headers["X-Bootstrap-Secret"].Count != 1) return Task.FromResult<IResult>(Results.Forbid());
        var authorization = Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : "";
        return bootstrap.CreateAsync(User, Request.Headers["X-Bootstrap-Secret"].ToString(), token, HttpContext.RequestAborted);
    }

    /**
     * Supplies live standing only to the separately authenticated Attractions service.
     * @param request Provider identity already validated by that service.
     * @return Access status/role; unknown accounts are deactivated and never implicitly provisioned.
     */
    [HttpPost("access")]
    [AllowAnonymous]
    public async Task<IResult> Access(AccessRequest request)
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: 400, title: "Account lookup requires HTTPS.");
        var authorization = Request.Headers.Authorization;
        if (authorization.Count != 1 || !authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            !SecretVerifier.Matches(configuration["UsersAccess:ServiceToken"], authorization.ToString()[7..])) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Issuer) || string.IsNullOrWhiteSpace(request.Subject)) return Results.BadRequest();
        var account = await repository.FindAsync(request.Issuer, request.Subject, HttpContext.RequestAborted);
        return Results.Ok(account is null ? new AccessResponse("deactivated", "traveller") : new AccessResponse(
            account.Status.ToString().ToLowerInvariant(),
            account.Role == UserRole.User ? "traveller" : "administrator"));
    }

    /** Returns liveness only; it does not assert provider or PostgreSQL readiness. */
    [HttpGet("/health")]
    [AllowAnonymous]
    public IResult Health() => Results.Ok(new { status = "ok", service = "Users" });

    string AccessToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : "";
    }
}
