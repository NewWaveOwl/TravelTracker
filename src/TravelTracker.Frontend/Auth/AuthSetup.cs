namespace TravelTracker.Frontend.Auth;

/**
 * Tells components whether sign-in is available, so they can offer it or explain it is not set up.
 * @param Configured True when a Users API address is configured, so sign-in can be offered.
 */
public sealed record AuthSetup(bool Configured);
