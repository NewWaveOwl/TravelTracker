namespace TravelTracker.Frontend.Auth;

/**
 * Tells components whether sign-in is configured, so provider secrets such as the Auth0 client secret never reach components.
 * @param Configured True when a sign-in provider's settings were read, so sign-in can be offered.
 * @param Supabase Public Supabase settings, or null when another provider is selected.
 */
public sealed record AuthSetup(bool Configured, SupabaseSettings? Supabase = null);
