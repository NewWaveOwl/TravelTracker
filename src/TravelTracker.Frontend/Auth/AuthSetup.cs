namespace TravelTracker.Frontend.Auth;

/**
 * Tells components whether sign-in is available and which kind, so they can offer it or explain it is not set up.
 * @param Configured True when the selected sign-in provider is ready, so sign-in can be offered.
 * @param Supabase Public Supabase settings, or null when sign-in goes through the Users API instead.
 */
public sealed record AuthSetup(bool Configured, SupabaseSettings? Supabase = null);
