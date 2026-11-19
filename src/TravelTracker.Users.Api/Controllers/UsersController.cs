using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TravelTracker.Security.Authentication;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;
using IUserRepository = TravelTracker.Users.Data.Repositories.IUserRepository;
using ProfileRepository = TravelTracker.Users.Data.Repositories.Profile.IUserRepository;

namespace TravelTracker.Users.Api.Controllers;

/**
 * Exposes self-service profile reads and updates, administrator account listing and role changes, a one-time bootstrap action
 * and a credential-protected service lookup.
 * @param repository Current service-owned account store.
 * @param profiles Resolves accounts for the internal access contract.
 * @param userProfiles Coordinates self-service profile operations.
 * @param bootstrap Verifies and performs first-admin setup.
 * @param configuration Operator-owned service credential.
 * @param logger Records rejected service calls and role decisions without identities or credentials.
 */
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(IUserRepository repository, ProfileRepository profiles, UserProfileService userProfiles,
    BootstrapService bootstrap, IConfiguration configuration, ILogger<UsersController> logger) : ControllerBase
{
    private const string PicturePath = "/api/v1/users/me/picture";

    /**
     * Reads the private image for the validated caller without provisioning an account.
     * @return The stored PNG, or a missing-picture, authentication or standing failure.
     */
    [HttpGet("me/picture")]
    [Authorize(Policy = ScopePolicies.UsersRead)]
    public async Task<IResult> ReadPicture()
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Request.IsHttps) return Results.Problem(statusCode: 400, title: "Account access requires HTTPS.");
        if (CurrentIdentity() is not { } identity) return Results.Unauthorized();
        var (canRead, picture) = await profiles.ReadPictureAsync(identity, HttpContext.RequestAborted);
        if (!canRead) return Results.Forbid();
        if (picture is null) return Results.NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(picture, "image/png");
    }

    /**
     * Validates a raw image and replaces the active owner's existing picture atomically.
     * @return A private read reference or a validation, authorization or concurrency failure.
     */
    [HttpPut("me/picture")]
    [Authorize(Policy = ScopePolicies.UsersWrite)]
    public async Task<IResult> ReplacePicture()
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!Request.IsHttps) return Results.Problem(statusCode: 400, title: "Account access requires HTTPS.");
        if (CurrentIdentity() is not { } identity) return Results.Unauthorized();
        var cancellationToken = HttpContext.RequestAborted;
        var account = await profiles.FindByProviderIdentityAsync(identity, cancellationToken);
        if (account?.Status != UserStatus.Active) return Results.Forbid();

        var (status, picture) = await ProfilePictureUpload.DecodeAsync(Request, cancellationToken);
        if (picture is null) return Results.Problem(statusCode: status, title: status switch
        {
            StatusCodes.Status413PayloadTooLarge => "The profile picture exceeds the byte or dimension limit.",
            StatusCodes.Status415UnsupportedMediaType => "Upload a single PNG or JPEG image with its matching media type.",
            _ => "The profile picture is empty or malformed."
        });

        try
        {
            if (!await profiles.ReplacePictureAsync(identity, picture, cancellationToken)) return Results.Forbid();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: 409, title: "The profile changed. Reload it before trying again.");
        }
        Response.Headers.Location = PicturePath;
        return Results.NoContent();
    }

    /**
     * Reads or provisions the account belonging to the validated provider identity.
     * @return The caller's profile or an authentication or standing failure.
     */
    [HttpGet("me")]
    [Authorize(Policy = ScopePolicies.UsersRead)]
    public async Task<IResult> Me()
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account access requires HTTPS.");
        if (CurrentIdentity() is not { } identity) return Results.Unauthorized();

        var result = await userProfiles.ReadOwnProfileAsync(identity, HttpContext.RequestAborted);
        return MapProfileResult(result);
    }

    /**
     * Updates only the authenticated active account's explicitly supplied profile fields.
     * @param request The profile changes.
     * @return The persisted profile or a validation, standing or concurrency failure.
     */
    [HttpPatch("me")]
    [Authorize(Policy = ScopePolicies.UsersWrite)]
    public async Task<IResult> UpdateMe(UpdateUserProfileRequest request)
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account access requires HTTPS.");
        if (CurrentIdentity() is not { } identity) return Results.Unauthorized();

        var result = await userProfiles.UpdateOwnProfileAsync(identity, request, HttpContext.RequestAborted);
        return MapProfileResult(result);
    }

    /**
     * Looks up current account standing for an authorized backend.
     * @param userId The server-issued public account identifier.
     * @return Minimal account standing or 404 when the account does not exist.
     */
    [HttpGet("/internal/users/{userId:guid}/access")]
    [Authorize(Policy = ScopePolicies.UsersAccess)]
    public async Task<IResult> AccessById(Guid userId)
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account lookup requires HTTPS.");
        var account = await profiles.FindByPublicIdAsync(userId, HttpContext.RequestAborted);
        return account is null ? Results.NotFound() : Results.Ok(new UserAccessResponse(
            account.PublicId, account.Status.ToString().ToLowerInvariant(), RoleCode(account)));
    }

    /**
     * Resolves a backend-validated identity to a public ID without provisioning an account.
     * @param request The validated issuer and subject supplied by the authorized backend.
     * @return The existing public ID or 404.
     */
    [HttpPost("/internal/users/identity")]
    [Authorize(Policy = ScopePolicies.UsersAccess)]
    public async Task<IResult> ResolveIdentity(AccessRequest request)
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account lookup requires HTTPS.");
        if (string.IsNullOrWhiteSpace(request.Issuer) || string.IsNullOrWhiteSpace(request.Subject)) return Results.BadRequest();
        var identity = new ProviderIdentity(request.Issuer, request.Subject);
        var account = await profiles.FindByProviderIdentityAsync(identity, HttpContext.RequestAborted);
        return account is null ? Results.NotFound() : Results.Ok(new UserIdentityResponse(account.PublicId));
    }

    /**
     * Reads the provider identity supplied by bearer validation.
     * @return The validated identity, or null when a claim is missing.
     */
    private ProviderIdentity? CurrentIdentity()
    {
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)
            ? null : new ProviderIdentity(issuer, subject);
    }

    /**
     * Maps the private self-service profile for its account holder.
     * @param account The caller's account.
     * @return Editable fields and public account metadata.
     */
    private static SelfProfileResponse SelfProfile(UserAccount account) =>
        new(account.PublicId, account.Username, account.Email, account.Address,
            RoleCode(account), account.Status.ToString().ToLowerInvariant(), account.CreatedAt, account.UpdatedAt);

    /**
     * Maps an application outcome to its existing self-profile HTTP response.
     * @param result The profile service outcome.
     * @return The corresponding public HTTP response.
     */
    private static IResult MapProfileResult(UserProfileResult result) => result.Kind switch
    {
        UserProfileResultKind.Success when result.Account is { } account => Results.Ok(SelfProfile(account)),
        UserProfileResultKind.Forbidden => Results.Forbid(),
        UserProfileResultKind.ValidationFailure when result.ValidationErrors is { } errors => Results.ValidationProblem(errors),
        UserProfileResultKind.UsernameConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That username is already in use.",
            extensions: new Dictionary<string, object?> { ["code"] = "username_conflict" }),
        UserProfileResultKind.ConcurrencyConflict => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The profile changed. Reload it before trying again.",
            extensions: new Dictionary<string, object?> { ["code"] = "profile_concurrency_conflict" }),
        _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Account profile operation could not be confirmed.")
    };

    /**
     * Maps a domain role to its public code.
     *
     * @param account The account whose role is exposed.
     * @return "traveller" or "administrator".
     */
    private static string RoleCode(UserAccount account) =>
        account.Role == UserRole.User ? "traveller" : "administrator";

    /**
     * Extracts the bearer token from the Authorization header.
     *
     * @return The token, or an empty string when the header is absent or not a bearer credential.
     */
    private string BearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : "";
    }

    /**
     * Lists registered application accounts only after live administrator authorization in the Users store.
     * @return Public account profiles, or 403 without account data for an inactive/non-administrator caller.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.UsersAdmin)]
    public async Task<IResult> List()
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account administration requires HTTPS.");
        if (CurrentIdentity() is not { } actor) return Results.Unauthorized();
        var accounts = await repository.ListForAdministratorAsync(actor.Issuer, actor.Subject, HttpContext.RequestAborted);
        if (accounts is null) logger.LogWarning("Account list refused: the caller is not an active administrator.");
        return accounts is null ? Results.Forbid() : Results.Ok(accounts.Select(UserProfileResponse.From).ToList());
    }

    /**
     * Changes a registered account role with live administrator checks, optimistic role comparison and durable audit.
     * @param id Public target account identifier.
     * @param request Whitelisted desired/current roles and a required written reason.
     * @return Updated public profile, or safe validation, permission, absence or conflict feedback.
     */
    [HttpPatch("{id:guid}/role")]
    [Authorize(Policy = ScopePolicies.UsersAdmin)]
    public async Task<IResult> ChangeRole(Guid id, ChangeUserRoleRequest request)
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account administration requires HTTPS.");
        if (CurrentIdentity() is not { } actor) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Enter a reason for the role change."] });
        var result = await repository.ChangeRoleAsync(actor.Issuer, actor.Subject, id, request.Role, request.ExpectedRole, request.Reason, HttpContext.RequestAborted);
        logger.LogInformation("Role change to {Role} finished with outcome {Outcome}.", request.Role, result.Code);
        return result.Code switch
        {
            "changed" when result.Account is { } account => Results.Ok(UserProfileResponse.From(account)),
            "forbidden" => Results.Forbid(),
            "notfound" => Results.NotFound(),
            "conflict" => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The account role changed. Refresh the user list before trying again."),
            "unchanged" => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The account already has the requested role; nothing changed."),
            "last-administrator" => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The last active administrator cannot be demoted."),
            "invalid" => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The requested role change is not valid for this account."),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The role change could not be confirmed.")
        };
    }

    /**
     * Assigns the initial administrator only with both a valid user identity and the setup operator credential.
     * @return Confirmed profile or a safe authentication, authorization or initialization failure.
     */
    [HttpPost("bootstrap-first-administrator")]
    [Authorize(Policy = ScopePolicies.FirstAdministratorBootstrap)]
    public Task<IResult> Bootstrap()
    {
        if (!Request.IsHttps) return Task.FromResult<IResult>(Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Administrator setup requires HTTPS."));
        if (Request.Headers["X-Bootstrap-Secret"].Count != 1) return Task.FromResult<IResult>(Results.Forbid());
        return bootstrap.CreateAsync(User, Request.Headers["X-Bootstrap-Secret"].ToString(), HttpContext.RequestAborted);
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
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Account lookup requires HTTPS.");
        var authorization = Request.Headers.Authorization;
        if (authorization.Count != 1 || !authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            !SecretVerifier.Matches(configuration["UsersAccess:ServiceToken"], BearerToken()))
        {
            logger.LogWarning("Service access lookup rejected: the bearer credential was missing, wrong, or UsersAccess:ServiceToken is not configured or shorter than 32 characters.");
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.Issuer) || string.IsNullOrWhiteSpace(request.Subject)) return Results.BadRequest();
        var account = await repository.FindAsync(request.Issuer, request.Subject, HttpContext.RequestAborted);
        return Results.Ok(account is null ? new AccessResponse("deactivated", "traveller") : new AccessResponse(
            account.Status.ToString().ToLowerInvariant(),
            account.Role == UserRole.User ? "traveller" : "administrator"));
    }
}
