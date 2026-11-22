using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;

namespace TravelTracker.Frontend.Auth;

/** Registers antiforgery-protected Supabase forms and process-local cookie sessions. */
public static class SupabaseAuthEndpoints
{
    const string AuthLimit = "supabase-auth";

    /**
     * Registers public-key Supabase Auth and the existing revocable HttpOnly session cookie.
     * @param services Host service collection.
     * @param settings Valid public Supabase settings.
     * @return The same service collection.
     */
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, SupabaseSettings settings)
    {
        services.AddSingleton(settings);
        services.AddHttpClient<SupabaseAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(10))
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
    public static IEndpointRouteBuilder MapSupabaseAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", () => Results.Redirect("/sign-in"));
        endpoints.MapGet("/auth/register", () => Results.Redirect("/sign-in?mode=register"));
        endpoints.MapPost("/auth/login", SignInAsync).RequireRateLimiting(AuthLimit);
        endpoints.MapPost("/auth/register", SignUpAsync).RequireRateLimiting(AuthLimit);
        endpoints.MapPost("/auth/confirm-email", ConfirmEmailAsync).RequireRateLimiting(AuthLimit);
        endpoints.MapPost("/auth/change-credential", ChangeCredentialAsync).RequireRateLimiting(AuthLimit);
        endpoints.MapPost("/auth/logout", SignOutAsync);
        return endpoints;
    }

    /**
     * Reauthenticates the signed-in owner and submits one provider-owned credential change.
     * @param context The credential form request.
     * @param antiforgery Validates the submitted form token.
     * @param auth Supabase Auth client.
     * @param sessions Local session registry.
     * @param clock Supplies the current session time.
     * @param directory Resolves the configured Users API.
     * @param client Reads live profile standing with the current bearer.
     * @return A fixed redirect or a request rejection.
     */
    static async Task<IResult> ChangeCredentialAsync(HttpContext context, IAntiforgery antiforgery,
        SupabaseAuthClient auth, SessionRegistry sessions, TimeProvider clock, ServiceDirectory directory, ServiceClient client)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest();
        var user = context.User;
        var token = AccessTokenClaims.ActiveToken(user, clock.GetUtcNow());
        if (token is null || !sessions.IsActive(user)) return Results.Unauthorized();
        if (!AccessTokenClaims.GrantedScopes(user).Contains("users.write")) return Results.StatusCode(403);

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var operation = form["operation"].ToString();
        var email = form["loginEmail"].ToString().Trim();
        var currentPassword = form["currentPassword"].ToString();
        var newEmail = form["newEmail"].ToString().Trim();
        var newPassword = form["newPassword"].ToString();
        var confirmation = form["passwordConfirmation"].ToString();
        var validChange = operation switch
        {
            "email" => ValidCredentials(newEmail, currentPassword) && newPassword.Length == 0 && confirmation.Length == 0,
            "password" => newEmail.Length == 0 && newPassword.Length is >= 8 and <= 128 && newPassword == confirmation,
            _ => false
        };
        if (!validChange || !ValidCredentials(email, currentPassword) ||
            form.Any(field => field.Value.Count != 1 || field.Key is not
                ("operation" or "loginEmail" or "currentPassword" or "newEmail" or "newPassword" or "passwordConfirmation" or "__RequestVerificationToken")))
            return Results.Redirect("/account?credential=invalid");

        SupabaseSession? fresh = null;
        try
        {
            var cancellationToken = context.RequestAborted;
            var profile = await client.SendAsync<SelfUserProfile>(HttpMethod.Get,
                directory.ProtectedUrlFor("Users", "/api/v1/users/me"), null, cancellationToken, token);
            if (profile.Body?.Status != "active") return Results.StatusCode(403);

            fresh = await auth.SignInAsync(email, currentPassword, cancellationToken);
            if (fresh is null || fresh.User.FindFirstValue("sub") != user.FindFirstValue("sub"))
                return Results.Redirect("/account?credential=rejected");
            if (!sessions.IsActive(user)) return Results.Unauthorized();
            if (!await auth.ChangeCredentialAsync(fresh.AccessToken, operation == "email" ? newEmail : null,
                    operation == "password" ? newPassword : null, currentPassword, cancellationToken))
                return Results.Redirect("/account?credential=rejected");

            sessions.Revoke(SessionRegistry.IdFor(user));
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await EndProviderSessionAsync(auth, token);
            return Results.Redirect(operation == "email" ? "/account?credential=verify-email" : "/account?credential=password-changed");
        }
        catch (ApiError error) when (error.Status is 401 or 403)
        {
            return Results.StatusCode(error.Status.Value);
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return Results.Redirect("/account?credential=unavailable");
        }
        finally
        {
            if (fresh is not null) await EndProviderSessionAsync(auth, fresh.AccessToken);
        }
    }

    /**
     * Attempts bounded provider cleanup even after the browser disconnects.
     * @param auth Supabase Auth client.
     * @param token The token for the provider session to close.
     * @return A task that completes without leaking provider diagnostics.
     */
    static async Task EndProviderSessionAsync(SupabaseAuthClient auth, string token)
    {
        try { await auth.SignOutAsync(token, CancellationToken.None); }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) { }
    }

    /**
     * Validates an antiforgery-protected password form and issues a revocable local session.
     * @param context The form request.
     * @param antiforgery Validates the origin-bound form token.
     * @param auth Supabase Auth client.
     * @param sessions Local revocation registry.
     * @return A fixed redirect without provider diagnostics or credentials.
     */
    static async Task<IResult> SignInAsync(HttpContext context, IAntiforgery antiforgery, SupabaseAuthClient auth, SessionRegistry sessions)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The sign-in request could not be verified.");
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        if (!ValidCredentials(email, password)) return Results.Redirect("/sign-in?error=credentials");
        try
        {
            var session = await auth.SignInAsync(email, password, context.RequestAborted);
            if (session is null) return Results.Redirect("/sign-in?error=credentials");
            sessions.Revoke(SessionRegistry.IdFor(context.User));
            var identity = session.User.Identities.Single();
            // Provider claims cannot choose the frontend's token or revocation metadata.
            foreach (var claim in identity.Claims.Where(claim => claim.Type.StartsWith("traveltracker:", StringComparison.Ordinal)).ToArray())
                identity.RemoveClaim(claim);
            identity.AddClaims([
                new Claim(AccessTokenClaims.Token, session.AccessToken),
                new Claim(AccessTokenClaims.ExpiresAt, session.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                new Claim(AccessTokenClaims.GrantedScope, string.Join(' ', identity.FindAll("scope").Select(claim => claim.Value))),
                new Claim(SessionRegistry.SessionIdClaim, sessions.Create(session.ExpiresAt))
            ]);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, session.User,
                new AuthenticationProperties { ExpiresUtc = session.ExpiresAt, IsPersistent = false });
            return Results.Redirect("/account");
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            // Do not log exception details: auth provider responses can contain sensitive identity data.
            return Results.Redirect("/sign-in?error=unavailable");
        }
    }

    /**
     * Requests registration and directs the traveller to verify their email before signing in.
     * @param context The submitted form.
     * @param antiforgery Validates the origin-bound form token.
     * @param auth Supabase Auth client.
     * @return A fixed confirmation or error redirect.
     */
    static async Task<IResult> SignUpAsync(HttpContext context, IAntiforgery antiforgery, SupabaseAuthClient auth)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The registration request could not be verified.");
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        if (!ValidCredentials(email, password) || password.Length < 8 || password != form["passwordConfirmation"].ToString())
            return Results.Redirect("/sign-in?mode=register&error=registration");
        try
        {
            if (!await auth.SignUpAsync(email, password, context.RequestAborted))
                return Results.Redirect("/sign-in?mode=register&error=registration");
            return Results.Redirect("/sign-in?message=confirmation");
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return Results.Redirect("/sign-in?mode=register&error=unavailable");
        }
    }

    /**
     * Ends local access first, then asks Supabase to revoke the current provider session.
     * @param context The sign-out request.
     * @param antiforgery Validates the origin-bound form token.
     * @param auth Supabase Auth client.
     * @param sessions Local revocation registry.
     * @param clock Current session clock.
     * @return A signed-out page even when the provider is unavailable.
     */
    static async Task<IResult> SignOutAsync(HttpContext context, IAntiforgery antiforgery, SupabaseAuthClient auth, SessionRegistry sessions, TimeProvider clock)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The sign-out request could not be verified.");
        var token = AccessTokenClaims.ActiveToken(context.User, clock.GetUtcNow());
        sessions.Revoke(SessionRegistry.IdFor(context.User));
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        try
        {
            if (token is not null && !await auth.SignOutAsync(token, context.RequestAborted))
                return Results.Redirect("/account?signedout=local");
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return Results.Redirect("/account?signedout=local");
        }
        return Results.Redirect("/account");
    }

    /**
     * Exchanges a confirmation hash only after an explicit, antiforgery-protected confirmation.
     * @param context The confirmation form.
     * @param antiforgery Origin-bound form validator.
     * @param auth Provider verification client.
     * @return A clean sign-in URL without confirmation secrets or provider tokens.
     */
    static async Task<IResult> ConfirmEmailAsync(HttpContext context, IAntiforgery antiforgery, SupabaseAuthClient auth)
    {
        if (!await ValidFormAsync(context, antiforgery)) return Results.BadRequest("The confirmation request could not be verified.");
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var hash = form["token_hash"].ToString();
        var type = form["type"].ToString();
        if (type is not ("email" or "email_change") || hash.Length is < 16 or > 512 ||
            !hash.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            return Results.Redirect("/sign-in?error=confirmation");
        try
        {
            return await auth.ConfirmEmailAsync(hash, type, context.RequestAborted)
                ? Results.Redirect(type == "email_change" ? "/account?credential=link-accepted" : "/sign-in?message=confirmed")
                : Results.Redirect("/sign-in?error=confirmation");
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return Results.Redirect("/sign-in?error=unavailable");
        }
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
     * @param email Submitted email.
     * @param password Submitted password.
     * @return Whether both fields can be sent to the configured identity provider.
     */
    static bool ValidCredentials(string email, string password) => email.Length is > 0 and <= 254 &&
        MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && password.Length is > 0 and <= 128;
}
