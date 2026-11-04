namespace TravelTracker.Frontend.Auth;

/**
 * Tells components whether sign-in is configured, without exposing the settings themselves.
 *
 * @param Settings The Auth0 settings, or null while sign-in setup is pending.
 */
public sealed record AuthSetup(Auth0Settings? Settings)
{
    public bool Configured => Settings is not null;
}
