using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TravelTracker.Api.Authentication;

/**
 * Registers real bearer validation and least-privilege catalogue policies.
 */
public static class BearerAuthenticationExtensions
{
    /**
     * Configures the resource server without choosing or hosting an identity provider.
     * @param services The API service registrations.
     * @param configuration Environment-specific deployment configuration.
     * @return The services for further composition.
     */
    public static IServiceCollection AddApiBearerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BearerSettings>().Bind(configuration.GetSection(BearerSettings.SectionName))
            .Validate(settings => IsHttps(settings.Authority), "Bearer Authority must be an absolute HTTPS URL.")
            .Validate(settings => IsHttps(settings.Issuer), "Bearer Issuer must be an absolute HTTPS URL.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "Bearer Audience is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ScopeClaimType), "Bearer ScopeClaimType is required.")
            .Validate(settings => settings.ClockSkewSeconds is >= 0 and <= 120,
                "Bearer ClockSkewSeconds must be between 0 and 120.")
            .Validate(settings => settings.ValidAlgorithms is { Length: > 0 } && settings.ValidAlgorithms.All(
                algorithm => algorithm is "RS256" or "RS384" or "RS512" or "PS256" or "PS384" or "PS512"
                    or "ES256" or "ES384" or "ES512"), "Bearer ValidAlgorithms must list supported asymmetric JWT algorithms.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<BearerSettings>>((options, settings) =>
            {
                var bearer = settings.Value;
                options.Authority = bearer.Authority;
                options.Audience = bearer.Audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateIssuer = true,
                    ValidIssuer = bearer.Issuer,
                    IssuerValidator = (issuer, _, _) => string.Equals(issuer, bearer.Issuer, StringComparison.Ordinal)
                        ? issuer : throw new SecurityTokenInvalidIssuerException("Unexpected issuer."),
                    ValidateAudience = true,
                    ValidAudience = bearer.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(bearer.ClockSkewSeconds),
                    ValidAlgorithms = bearer.ValidAlgorithms,
                    NameClaimType = "sub"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        OpenIdConnectConfiguration? metadata;
                        try
                        {
                            metadata = context.Options.ConfigurationManager is null ? null :
                                await context.Options.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException ||
                            !context.HttpContext.RequestAborted.IsCancellationRequested)
                        {
                            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                .CreateLogger("TravelTracker.Authentication").LogWarning(
                                    "Bearer discovery metadata retrieval failed ({FailureType}). Check authority availability and configuration.",
                                    exception.GetType().Name);
                            context.Fail("Discovery metadata unavailable.");
                            return;
                        }
                        if (!string.Equals(metadata?.Issuer, bearer.Issuer, StringComparison.Ordinal))
                        {
                            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                .CreateLogger("TravelTracker.Authentication").LogWarning(
                                    "Bearer discovery issuer does not match configured Issuer. Check Authentication:Bearer settings.");
                            context.Fail("Discovery issuer mismatch.");
                            return;
                        }
                        // Never substitute a request header or body value for the validated subject.
                        var subjects = context.Principal?.FindAll("sub").ToArray();
                        if (subjects is not { Length: 1 } || string.IsNullOrWhiteSpace(subjects[0].Value))
                            context.Fail("A single subject is required.");
                    },
                    OnAuthenticationFailed = context =>
                    {
                        // Log only the failure category: exception messages may contain token details.
                        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("TravelTracker.Authentication").LogWarning(
                                "Bearer validation failed ({FailureType}). Check issuer, audience, lifetime and signing settings.",
                                context.Exception.GetType().Name);
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>().Configure<IOptions<BearerSettings>>((options, settings) =>
        {
            foreach (var scope in new[] { ScopePolicies.CatalogueRead, ScopePolicies.CatalogueWrite })
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser()
                    .RequireAssertion(context => ScopePolicies.HasScope(context.User, settings.Value.ScopeClaimType, scope)));
            options.DefaultPolicy = options.GetPolicy(ScopePolicies.CatalogueRead)!;
            options.FallbackPolicy = options.DefaultPolicy;
        });
        return services;
    }

    /**
     * Checks a metadata or issuer address without allowing insecure discovery.
     * @param value The configured address.
     * @return Whether it is an absolute HTTPS URL.
     */
    private static bool IsHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
