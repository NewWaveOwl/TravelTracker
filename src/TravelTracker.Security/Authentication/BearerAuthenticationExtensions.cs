using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Security.Authentication;

/**
 * Registers real bearer validation and the least-privilege scope policies for every TravelTracker API.
 */
public static class BearerAuthenticationExtensions
{
    /**
     * Configures the resource server to verify tokens against the Users API's public key (Local) or Supabase's published signing keys (Supabase).
     * @param services The API service registrations.
     * @param configuration Environment-specific deployment configuration.
     * @return The services for further composition.
     */
    public static IServiceCollection AddApiBearerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BearerSettings>().Bind(configuration.GetSection(BearerSettings.SectionName))
            .PostConfigure(settings =>
            {
                // the Users API signs with the private key, so it can verify with the matching public half
                if (settings.Provider == BearerSettings.LocalProvider && string.IsNullOrWhiteSpace(settings.PublicKey) && configuration["Authentication:Issuer:PrivateKey"] is { Length: > 0 } privateKey)
                    settings.PublicKey = PublicPemFromPrivate(privateKey);
            })
            .Validate(settings => settings.Provider is BearerSettings.LocalProvider or BearerSettings.SupabaseProvider,
                "Bearer Provider must be Local or Supabase.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer), "Bearer Issuer is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "Bearer Audience is required.")
            .Validate(settings => settings.Provider != BearerSettings.LocalProvider || ParsePublicKey(settings.PublicKey) is not null,
                "Bearer PublicKey must be a PEM-encoded ECDSA public key. Set Authentication:Bearer:PublicKey.")
            .Validate(settings => settings.Provider != BearerSettings.SupabaseProvider || IsSupabaseIssuer(settings),
                "Supabase Authority and Issuer must identify the same HTTPS /auth/v1 issuer, without a trailing slash on Issuer.")
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
                var supabase = bearer.Provider == BearerSettings.SupabaseProvider;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                if (supabase)
                {
                    options.Authority = bearer.Authority;
                    options.Audience = bearer.Audience;
                    options.RequireHttpsMetadata = true;
                    // Supabase publishes JWKS directly; it is not an OIDC discovery document.
                    options.MetadataAddress = bearer.Issuer + "/.well-known/jwks.json";
                    options.Backchannel ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(options.MetadataAddress,
                        new SupabaseSigningConfigurationRetriever(bearer.Issuer), new HttpDocumentRetriever(options.Backchannel) { RequireHttps = true });
                }
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKeys = supabase ? null : [new ECDsaSecurityKey(ParsePublicKey(bearer.PublicKey))],
                    IssuerValidator = supabase
                        ? (issuer, _, _) => string.Equals(issuer, bearer.Issuer, StringComparison.Ordinal)
                            ? issuer : throw new SecurityTokenInvalidIssuerException("Unexpected issuer.")
                        : null,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateIssuer = true,
                    ValidIssuer = bearer.Issuer,
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
                        if (supabase && !await SupabaseMetadataMatchesAsync(context, bearer)) return;
                        // Never substitute a request header or body value for the validated subject.
                        var subjects = context.Principal?.FindAll("sub").ToArray();
                        if (subjects is not { Length: 1 } || string.IsNullOrWhiteSpace(subjects[0].Value))
                        {
                            // log the category only: never the subject value or the token.
                            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                .CreateLogger("TravelTracker.Authentication").LogWarning(
                                    "Bearer token rejected: the subject claim is missing, blank or ambiguous.");
                            context.Fail("A single subject is required.");
                            return;
                        }
                        if (supabase &&
                            (!Guid.TryParse(subjects[0].Value, out var subject) || subject == Guid.Empty ||
                             context.Principal!.FindAll("role").Select(claim => claim.Value).ToArray() is not ["authenticated"] ||
                             context.Principal.FindAll("is_anonymous").Any(claim =>
                                 string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase))))
                        {
                            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                .CreateLogger("TravelTracker.Authentication").LogWarning(
                                    "Bearer token rejected: not a non-anonymous authenticated Supabase user.");
                            context.Fail("A non-anonymous authenticated Supabase user is required.");
                        }
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
            foreach (var scope in new[] { ScopePolicies.CatalogueRead, ScopePolicies.CatalogueWrite, ScopePolicies.SettingsRead, ScopePolicies.SettingsWrite, ScopePolicies.TripsRead, ScopePolicies.TripsWrite })
                options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser()
                    .RequireAssertion(context => ScopePolicies.HasScope(context.User, settings.Value.ScopeClaimType, scope)));
            options.DefaultPolicy = options.GetPolicy(ScopePolicies.CatalogueRead)!;
            // the fallback guards endpoints that declare no policy. the framework also applies it to requests that match no route;
            // for those the resource is the HttpContext with no endpoint. they pass through so routing answers 404 instead of 401/403.
            // matched endpoints keep the scope requirement.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAssertion(context =>
                context.Resource is HttpContext { } request && request.GetEndpoint() is null ||
                context.User.Identity?.IsAuthenticated == true &&
                ScopePolicies.HasScope(context.User, settings.Value.ScopeClaimType, ScopePolicies.CatalogueRead)).Build();
        });
        return services;
    }

    /**
     * Confirms the signing-key metadata still names the configured issuer, so a swapped key document cannot vouch for tokens.
     * @param context The validated token context; it is failed when the metadata is unavailable or names another issuer.
     * @param bearer The configured bearer settings.
     * @return Whether the metadata matches the configured issuer.
     */
    private static async Task<bool> SupabaseMetadataMatchesAsync(TokenValidatedContext context, BearerSettings bearer)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TravelTracker.Authentication");
        OpenIdConnectConfiguration? metadata;
        try
        {
            metadata = context.Options.ConfigurationManager is null ? null :
                await context.Options.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
            !context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Bearer discovery metadata retrieval failed ({FailureType}). Check authority availability and configuration.",
                exception.GetType().Name);
            context.Fail("Discovery metadata unavailable.");
            return false;
        }
        if (string.Equals(metadata?.Issuer, bearer.Issuer, StringComparison.Ordinal)) return true;
        logger.LogWarning("Bearer discovery issuer does not match configured Issuer. Check Authentication:Bearer settings.");
        context.Fail("Discovery issuer mismatch.");
        return false;
    }

    /**
     * Pins the Supabase key endpoint to a credential-free HTTPS Auth issuer.
     * @param settings Resource-server provider configuration.
     * @return Whether the authority and exact issuer describe the same Auth service.
     */
    private static bool IsSupabaseIssuer(BearerSettings settings) =>
        Uri.TryCreate(settings.Issuer, UriKind.Absolute, out var issuer) && issuer.Scheme == Uri.UriSchemeHttps &&
        issuer.UserInfo.Length == 0 && issuer.Query.Length == 0 && issuer.Fragment.Length == 0 &&
        issuer.AbsolutePath == "/auth/v1" && settings.Authority.TrimEnd('/') == settings.Issuer;

    /**
     * Reads a PEM public key.
     * @param pem The configured key.
     * @return The key, or null when it is blank or not an ECDSA public key.
     */
    private static ECDsa? ParsePublicKey(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) return null;
        try
        {
            var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    /**
     * Derives the public half of a PEM private key.
     * @param pem The configured private key.
     * @return The public key PEM, or blank when the private key is not an ECDSA key, which validation then reports.
     */
    private static string PublicPemFromPrivate(string pem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return key.ExportSubjectPublicKeyInfoPem();
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return "";
        }
    }
}
