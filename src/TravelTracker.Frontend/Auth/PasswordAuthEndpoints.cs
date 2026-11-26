using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace TravelTracker.Frontend.Auth;

/** Registers antiforgery-protected password forms and process-local cookie sessions. */
public static class PasswordAuthEndpoints
{
    const string AuthLimit = "password-auth";

    /**
     * Registers the Users API sign-in client and the revocable HttpOnly session cookie.
     * @param services Host service collection.
     * @return The same service collection.
     */
    public static IServiceCollection AddPasswordAuth(this IServiceCollection services)
    {
        services.AddHttpClient<UsersAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/auth/login";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                if (!context.HttpContext.RequestServices.GetRequiredService<SessionRegistry>().IsActive(context.Principal!))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    /**
     * Maps sign-in, registration and sign-out without collecting credentials in an interactive circuit.
     * @param endpoints Application route builder.
     * @return The same route builder.
     */
    public static IEndpointRouteBuilder MapPasswordAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", () => Results.Redirect("/sign-in"));
        endpoints.MapGet("/auth/register", () => Results.Redirect("/sign-in?mode=register"));
        endpoints.MapPost("/auth/login", SignInAsync).RequireRateLimiting(AuthLimit);
        endpoints.MapPost("/auth/register", SignUpAsync).RequireRateLimiting(AuthLimit);
        endpoints.MapPost("/auth/logout", SignOutAsync);
        return endpoints;
    }

    /**
     * Validates an antiforgery-protected password form and issues a revocable local session.
     * @param context The form request.
     * @param antiforgery Validates the origin-bound form token.
     * @param auth Users API client.
     * @param sessions Local revocation registry.
     * @return A fixed redirect without service diagnostics or credentials.
     */
    static async Task<IResult> SignInAsync(HttpContext context, IAntiforgery antiforgery, UsersAuthClient auth, SessionRegistry sessions)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The sign-in request could not be verified.");
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var username = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        if (!ValidCredentials(username, password)) return Results.Redirect("/sign-in?error=credentials");
        try
        {
            var session = await auth.SignInAsync(username, password, context.RequestAborted);
            if (session is null) return Results.Redirect("/sign-in?error=credentials");
            await StartSessionAsync(context, session, sessions);
            return Results.Redirect("/account");
        }
        catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // the Users API locked this username after repeated failures
            return Results.Redirect("/sign-in?error=rate-limit");
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            // the type only: responses can carry sensitive identity data
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TravelTracker.Frontend.Auth")
                .LogError("Sign-in could not reach the Users API ({FailureType}).", error.GetType().Name);
            return Results.Redirect("/sign-in?error=unavailable");
        }
    }

    /**
     * Creates an account and signs it straight in.
     * @param context The submitted form.
     * @param antiforgery Validates the origin-bound form token.
     * @param auth Users API client.
     * @param sessions Local revocation registry.
     * @return A fixed redirect.
     */
    static async Task<IResult> SignUpAsync(HttpContext context, IAntiforgery antiforgery, UsersAuthClient auth, SessionRegistry sessions)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The registration request could not be verified.");
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var username = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        if (!ValidCredentials(username, password) || password.Length < 6 || password != form["passwordConfirmation"].ToString())
            return Results.Redirect("/sign-in?mode=register&error=registration");
        try
        {
            var session = await auth.RegisterAsync(username, password, context.RequestAborted);
            if (session is null) return Results.Redirect("/sign-in?mode=register&error=registration");
            await StartSessionAsync(context, session, sessions);
            return Results.Redirect("/account");
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TravelTracker.Frontend.Auth")
                .LogError("Registration could not reach the Users API ({FailureType}).", error.GetType().Name);
            return Results.Redirect("/sign-in?mode=register&error=unavailable");
        }
    }

    /**
     * Ends local access. Nothing is held elsewhere, so there is no provider session to revoke.
     * @param context The sign-out request.
     * @param antiforgery Validates the origin-bound form token.
     * @param sessions Local revocation registry.
     * @return A redirect to the account page.
     */
    static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery, SessionRegistry sessions)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The sign-out request could not be verified.");
        sessions.Revoke(SessionRegistry.IdFor(context.User));
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/account");
    }

    /**
     * Replaces any previous session for this browser with one for the accepted sign-in.
     * @param context The request that signed in.
     * @param session The accepted sign-in.
     * @param sessions Local revocation registry.
     * @return A task that completes when the cookie is issued.
     */
    static async Task StartSessionAsync(HttpContext context, PasswordSession session, SessionRegistry sessions)
    {
        sessions.Revoke(SessionRegistry.IdFor(context.User));
        var identity = session.User.Identities.Single();
        identity.AddClaims([
            new Claim(AccessTokenClaims.Token, session.AccessToken),
            new Claim(AccessTokenClaims.ExpiresAt, session.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new Claim(AccessTokenClaims.GrantedScope, string.Join(' ', identity.FindAll("scope").Select(claim => claim.Value))),
            new Claim(SessionRegistry.SessionIdClaim, sessions.Create(session.ExpiresAt))
        ]);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, session.User,
            new AuthenticationProperties { ExpiresUtc = session.ExpiresAt, IsPersistent = false });
    }

    /**
     * Refuses non-form, oversized or cross-site credential submissions before reading a password.
     * @param context Request context.
     * @param antiforgery Form-token validator.
     * @return Whether the credential form can be processed.
     */
    static async Task<bool> ValidFormAsync(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.HasFormContentType || context.Request.ContentLength is > 16384) return false;
        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 16384;
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    /**
     * Applies bounded local syntax checks without changing password bytes.
     * @param username Submitted username.
     * @param password Submitted password.
     * @return Whether both fields can be sent to the Users API.
     */
    static bool ValidCredentials(string username, string password) => username.Length is > 0 and <= 32 && password.Length is > 0 and <= 128;
}
