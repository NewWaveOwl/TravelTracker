using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Settings;

namespace TravelTracker.Frontend.Services;

/**
 * holds the signed-in traveller's saved preferences for this circuit so client features can apply them.
 * values are cleared on every authentication change, so one account never inherits another account's preferences.
 * @param client Reads the saved preferences through the protected Settings contract.
 * @param authentication Signals sign-in and sign-out changes.
 * @param logger Records failed reads; callers keep their defaults when a read fails.
 */
public sealed class UserPreferences(SettingsClient client, AuthenticationStateProvider authentication, ILogger<UserPreferences> logger) : IDisposable
{
    bool subscribed;
    bool disposed;

    /** the last confirmed preference values for the current sign-in, or null before a successful read or save. */
    public SettingsDto? Current { get; private set; }

    /**
     * reads the saved values once per sign-in; later calls reuse them without a request.
     * @param cancellationToken Cancels a closed view.
     * @return The saved preferences, or null when they could not be read; the failure is logged.
     */
    public async Task<SettingsDto?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!subscribed)
        {
            authentication.AuthenticationStateChanged += AuthenticationChanged;
            subscribed = true;
        }
        if (Current is not null) return Current;
        try
        {
            var revision = await client.GetAsync(cancellationToken);
            if (!disposed) Current = revision.Settings;
            return revision.Settings;
        }
        catch (ApiError failure)
        {
            logger.LogWarning("Saved preferences could not be read as {Kind} (status {Status}); client defaults apply", failure.Kind, failure.Status);
            return null;
        }
    }

    /**
     * stores values the service just confirmed, so features pick them up without another read.
     * @param settings The server-confirmed preference values.
     */
    public void Remember(SettingsDto settings) => Current = settings;

    /** drops the held values so the next load asks the service again. */
    public void Forget() => Current = null;

    void AuthenticationChanged(Task<AuthenticationState> _) => Forget();

    /** unsubscribes from authentication changes when the circuit closes. */
    public void Dispose()
    {
        disposed = true;
        if (subscribed) authentication.AuthenticationStateChanged -= AuthenticationChanged;
    }
}
