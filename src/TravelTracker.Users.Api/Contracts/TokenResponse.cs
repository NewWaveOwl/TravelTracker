namespace TravelTracker.Users.Api.Contracts;

/**
 * An issued access token and what it grants.
 * @param AccessToken The signed JWT.
 * @param ExpiresIn Seconds until the token expires.
 * @param Scope Space-separated granted scopes.
 */
public sealed record TokenResponse(string AccessToken, int ExpiresIn, string Scope)
{
    /** Always Bearer. */
    public string TokenType => "Bearer";
}
