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
        })
        .AddOpenIdConnect(options =>
        {
            options.Authority = settings.Authority;
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
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
                        return Task.CompletedTask;
                    }
                    var expiresAt = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().AddSeconds(int.TryParse(context.TokenEndpointResponse?.ExpiresIn, out var seconds) ? seconds : 0);
                    ((ClaimsIdentity)context.Principal!.Identity!).AddClaims(
                    [
                        new Claim(AccessTokenClaims.Token, token),
                        new Claim(AccessTokenClaims.ExpiresAt, expiresAt.ToUnixTimeSeconds().ToString()),
                    ]);
                    // the session ends when the access token does; there is no silent renewal
                    context.Properties!.ExpiresUtc = expiresAt;
                    context.Properties.IsPersistent = false;
                    return Task.CompletedTask;
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
        endpoints.MapGet("/auth/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = AccountPath }, [OpenIdConnectDefaults.AuthenticationScheme]));
        // the antiforgery middleware only records its verdict; an endpoint that binds no form must check it itself.
        // the refusal needs a body, or status-code-page re-execution runs antiforgery a second time and throws
        endpoints.MapPost("/auth/logout", (HttpContext context) => context.Features.Get<IAntiforgeryValidationFeature>()?.IsValid == true
            ? Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme])
            : Results.Text("The sign-out request could not be verified. Go back and try again.", statusCode: StatusCodes.Status400BadRequest)).WithMetadata(new RequireAntiforgeryTokenAttribute());
        return endpoints;
    }
}
