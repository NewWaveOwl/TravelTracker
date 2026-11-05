using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TravelTracker.Frontend.Auth;

/** Wires Auth0 hosted sign-in (authorization code with PKCE) and the sign-in and sign-out endpoints. */
public static class AuthExtensions
{
    const string AccountPath = "/account";

    /**
     * Adds cookie and OpenID Connect authentication for Auth0.
     *
     * @param services The service collection.
     * @param settings The validated Auth0 settings.
     * @param logger Receives sign-in failures and configuration, never token values.
     * @return The same service collection.
     */
    public static IServiceCollection AddAuth0(this IServiceCollection services, Auth0Settings settings, ILogger logger)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                if (!context.HttpContext.RequestServices.GetRequiredService<SessionRegistry>().IsActive(context.Principal!))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
        })
        .AddOpenIdConnect(options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.BackchannelTimeout = TimeSpan.FromSeconds(10);
            options.UsePkce = true;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.MapInboundClaims = false;
            // SameAsRequest keeps plain-http localhost development working; over https the cookies are Secure
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.Scope.Clear();
            foreach (var scope in settings.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                options.Scope.Add(scope);
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.SetParameter("audience", settings.Audience);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var token = context.TokenEndpointResponse?.AccessToken;
                    if (string.IsNullOrEmpty(token))
                    {
                        logger.LogWarning("Sign-in completed without an access token for audience {Audience}", settings.Audience);
                        context.Fail("An access token is required.");
                        return Task.CompletedTask;
                    }
                    var expiresAt = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().AddSeconds(int.TryParse(context.TokenEndpointResponse?.ExpiresIn, out var seconds) ? seconds : 0);
                    var identity = (ClaimsIdentity)context.Principal!.Identity!;
                    // Provider claims cannot choose server-owned token or session metadata.
                    foreach (var claim in context.Principal.FindAll(AccessTokenClaims.Token).Concat(context.Principal.FindAll(AccessTokenClaims.ExpiresAt)).Concat(context.Principal.FindAll(AccessTokenClaims.GrantedScope)).Concat(context.Principal.FindAll(SessionRegistry.SessionIdClaim)).ToArray())
                        ((ClaimsIdentity)claim.Subject!).RemoveClaim(claim);
                    identity.AddClaims(
                    [
                        new Claim(AccessTokenClaims.Token, token),
                        new Claim(AccessTokenClaims.ExpiresAt, expiresAt.ToUnixTimeSeconds().ToString()),
                        // OAuth permits scope omission when the granted scopes equal those requested.
                        new Claim(AccessTokenClaims.GrantedScope, context.TokenEndpointResponse?.Scope ?? settings.Scope),
                    ]);
                    // the session ends when the access token does; there is no silent renewal
                    context.Properties!.ExpiresUtc = expiresAt;
                    context.Properties.IsPersistent = false;
                    return Task.CompletedTask;
                },
                OnTicketReceived = async context =>
                {
                    // Register only after nonce, correlation and protocol validation have succeeded.
                    var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionRegistry>();
                    // OIDC handles its callback before default cookie authentication populates HttpContext.User.
                    var previous = await context.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    if (previous.Principal is not null)
                        sessions.Revoke(SessionRegistry.IdFor(previous.Principal));
                    ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim(SessionRegistry.SessionIdClaim, sessions.Create(context.Properties!.ExpiresUtc!.Value)));
                },
                OnRedirectToIdentityProviderForSignOut = context =>
                {
                    var returnTo = Uri.EscapeDataString($"{context.Request.Scheme}://{context.Request.Host}/");
                    context.Response.Redirect($"{settings.Authority}v2/logout?client_id={Uri.EscapeDataString(settings.ClientId)}&returnTo={returnTo}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    // the callback carries provider diagnostics; keep them in the log and show only a fixed code
                    var code = LoginFailure.CodeFor(context.Request.Query["error"], context.Request.Query["error_description"]);
                    logger.LogWarning(context.Failure, "Sign-in failed as {Code} (provider error {Error})", code, context.Request.Query["error"].ToString());
                    context.Response.Redirect($"{AccountPath}?signin={code}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                },
            };
        });
        return services;
    }

    /**
     * Maps the endpoints that start and end the hosted session.
     *
     * @param endpoints The route builder.
     * @return The same route builder.
     */
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", async (HttpContext context, ILoggerFactory loggers) =>
        {
            try
            {
                await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = AccountPath });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // most likely the tenant's discovery document could not be fetched; nothing was sent to the browser yet
                loggers.CreateLogger("TravelTracker.Frontend.Auth").LogError(exception, "Could not start Auth0 sign-in");
                context.Response.Redirect($"{AccountPath}?signin={LoginFailure.StartFailed}");
            }
        });
        // the antiforgery middleware only records its verdict; an endpoint that binds no form must check it itself.
        // the refusal needs a body, or status-code-page re-execution runs antiforgery a second time and throws
        endpoints.MapPost("/auth/logout", async (HttpContext context, ILoggerFactory loggers) =>
        {
            if (context.Features.Get<IAntiforgeryValidationFeature>()?.IsValid != true)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("The sign-out request could not be verified. Go back and try again.");
                return;
            }
            // the local session ends first, so a provider failure below can never leave the user signed in
            context.RequestServices.GetRequiredService<SessionRegistry>().Revoke(SessionRegistry.IdFor(context.User));
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            try
            {
                await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = "/" });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                loggers.CreateLogger("TravelTracker.Frontend.Auth").LogError(exception, "Signed out locally but Auth0 logout could not be started");
                context.Response.Redirect($"{AccountPath}?signedout=local");
            }
        }).WithMetadata(new RequireAntiforgeryTokenAttribute());
        return endpoints;
    }
}
