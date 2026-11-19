using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Api.Services;

/**
 * Signs ES256 access tokens with a private key that only ever lives in configuration, never on disk or in the database.
 * @param configuration Supplies the issuer, audience, lifetime and the PEM private key.
 * @param timeProvider Supplies the issue time.
 */
public sealed class TokenIssuer(IConfiguration configuration, TimeProvider timeProvider)
{
    /** Subject of the machine identity that other TravelTracker backends use to look up account standing. */
    public const string ServiceSubject = "traveltracker-service";

    private static readonly string[] TravellerScopes =
    [
        "catalogue.read", "catalogue.write", "users.read", "users.write", "users.admin",
        "settings.read", "settings.write", "trips.read", "trips.write"
    ];

    private readonly Lazy<SigningCredentials?> credentials = new(() =>
    {
        var pem = configuration["Authentication:Issuer:PrivateKey"];
        if (string.IsNullOrWhiteSpace(pem)) return null;
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256);
    });

    /** Whether a signing key is configured, so callers can answer 503 instead of throwing. */
    public bool Configured => credentials.Value is not null;

    /**
     * Issues a token for an active account, with scopes from its stored role.
     * @param account The signed-in account.
     * @return The token, or null when the account is not active.
     */
    public TokenResponse? ForAccount(UserAccount account) => account.Status != UserStatus.Active ? null
        : Issue(account.ProviderIdentity.Subject, string.Join(' ', TravellerScopes));

    /**
     * Issues the backend-only token that carries users.access and nothing else.
     * @return The service token.
     */
    public TokenResponse ForService() => Issue(ServiceSubject, "users.access");

    private TokenResponse Issue(string subject, string scope)
    {
        var minutes = configuration.GetValue("Authentication:Issuer:LifetimeMinutes", 60);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = configuration["Authentication:Bearer:Issuer"],
            Audience = configuration["Authentication:Bearer:Audience"],
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new("sub", subject),
                new("scope", scope),
            ]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(minutes),
            SigningCredentials = credentials.Value ?? throw new InvalidOperationException("Authentication:Issuer:PrivateKey is not configured."),
        });
        return new TokenResponse(token, minutes * 60, scope);
    }
}
