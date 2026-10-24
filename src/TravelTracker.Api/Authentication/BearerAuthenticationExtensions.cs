using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "sub"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Never substitute a request header or body value for the validated subject.
                        var subjects = context.Principal?.FindAll("sub").ToArray();
                        if (subjects is not { Length: 1 } || string.IsNullOrWhiteSpace(subjects[0].Value))
                            context.Fail("A single subject is required.");
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>().Configure<IOptions<BearerSettings>>((options, settings) =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            foreach (var scope in new[] { ScopePolicies.CatalogueRead, ScopePolicies.CatalogueWrite })
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser()
                    .RequireAssertion(context => ScopePolicies.HasScope(context.User, settings.Value.ScopeClaimType, scope)));
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
