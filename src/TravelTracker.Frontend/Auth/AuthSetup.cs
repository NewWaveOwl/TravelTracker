namespace TravelTracker.Frontend.Auth;

/**
 * Tells components whether sign-in is configured, without exposing the settings themselves.
 *
 * @param Settings Optional legacy Auth0 settings.
 * @param Supabase Public Supabase settings, or null when another provider is selected.
 */
public sealed record AuthSetup(Auth0Settings? Settings, SupabaseSettings? Supabase = null)
{
    public bool Configured => Settings is not null || Supabase is not null;
}
