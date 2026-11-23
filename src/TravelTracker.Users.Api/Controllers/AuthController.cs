using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Api.Services;
using TravelTracker.Users.Data.Repositories.Credentials;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Api.Controllers;

/**
 * Registers password accounts and issues access tokens. Passwords are hashed with ASP.NET Core's PasswordHasher and only the hash is stored.
 * @param credentials Password credential store.
 * @param tokens Signs the access tokens.
 * @param configuration Supplies the issuer and the service credential.
 * @param timeProvider Supplies the registration time.
 * @param logger Records outcomes without usernames, passwords or tokens.
 */
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(ICredentialRepository credentials, TokenIssuer tokens, IConfiguration configuration,
    TimeProvider timeProvider, ILogger<AuthController> logger) : ControllerBase
{
    /** Hash checked when the login is unknown, so a miss costs the same as a wrong password. */
    private static readonly string DummyHash = PasswordCredentials.Hash("not-a-real-password");

    /**
     * Creates a traveller account and signs it in.
     * @param request The username and password.
     * @return The token on success, or a validation, conflict or availability failure.
     */
    [HttpPost("register")]
    public async Task<IResult> Register(CredentialsRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        var username = request.Username.Trim();
        var errors = new Dictionary<string, string[]>();
        // the domain owns the username rules, so ask it rather than repeating them
        var probe = new UserAccount(Guid.CreateVersion7(), new ProviderIdentity("registration", "probe"), UserRole.User, UserStatus.Active, timeProvider.GetUtcNow(), username);
        if (username.Length == 0) errors["username"] = ["Enter a username."];
        else if (probe.Validate().TryGetValue("username", out var usernameErrors)) errors["username"] = usernameErrors;
        if (PasswordCredentials.Validate(request.Password) is { } passwordError) errors["password"] = [passwordError];
        if (errors.Count != 0) return Results.ValidationProblem(errors);

        var issuer = configuration["Authentication:Bearer:Issuer"]!;
        var account = await credentials.RegisterAsync(issuer, username, PasswordCredentials.Hash(request.Password),
            timeProvider.GetUtcNow(), HttpContext.RequestAborted);
        if (account is null)
        {
            logger.LogInformation("Registration refused: the username is already registered.");
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "That username is already taken.");
        }
        logger.LogInformation("Registered a new traveller account.");
        return Results.Json(tokens.ForAccount(account), statusCode: StatusCodes.Status201Created);
    }

    /**
     * Checks a password and issues a token.
     * @param request The username and password.
     * @return The token, 401 for any wrong credential, or 403 for an account that is not active.
     */
    [HttpPost("login")]
    public async Task<IResult> Login(CredentialsRequest request)
    {
        if (Unavailable() is { } unavailable) return unavailable;
        var stored = await credentials.FindAsync(request.Username, HttpContext.RequestAborted);
        var result = PasswordCredentials.Verify(stored?.PasswordHash ?? DummyHash, request.Password);
        if (stored is null || result == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Sign-in refused: unknown username or wrong password.");
            return Results.Unauthorized();
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            await credentials.UpdateHashAsync(stored.Account.PublicId, PasswordCredentials.Hash(request.Password), HttpContext.RequestAborted);
        if (tokens.ForAccount(stored.Account) is not { } token)
        {
            logger.LogWarning("Sign-in refused: the account is {Status}.", stored.Account.Status);
            return Results.Forbid();
        }
        return Results.Ok(token);
    }

    /**
     * Issues the users.access token to a backend that presents the operator-owned service credential.
     * @return The service token, or 401 for a missing or wrong credential.
     */
    [HttpPost("service-token")]
    public IResult ServiceToken()
    {
        if (Unavailable() is { } unavailable) return unavailable;
        var authorization = Request.Headers.Authorization.ToString();
        var supplied = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..] : "";
        if (!SecretVerifier.Matches(configuration["UsersAccess:ServiceToken"], supplied))
        {
            logger.LogWarning("Service token refused: the credential was missing, wrong, or UsersAccess:ServiceToken is not configured or shorter than 32 characters.");
            return Results.Unauthorized();
        }
        return Results.Ok(tokens.ForService());
    }

    private IResult? Unavailable()
    {
        if (!Request.IsHttps) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Sign-in requires HTTPS.");
        if (tokens.Configured && !string.IsNullOrWhiteSpace(configuration["Authentication:Bearer:Issuer"])) return null;
        logger.LogError("Sign-in is unavailable: Authentication:Issuer:PrivateKey or Authentication:Bearer:Issuer is not configured.");
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Sign-in is not configured.");
    }
}
