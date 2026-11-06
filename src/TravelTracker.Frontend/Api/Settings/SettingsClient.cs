using System.Net;

namespace TravelTracker.Frontend.Api.Settings;

/**
 * reads and replaces the signed-in traveller's own preferences through the protected Settings contract.
 * @param client Sends bounded calls with the current session token; the identity comes only from that token.
 */
public sealed class SettingsClient(ProtectedClient client)
{
    const string Path = "/api/v1/settings";

    /**
     * reads the caller's saved preferences, or the service defaults before the first save.
     * @param cancellationToken Cancels a closed view.
     * @return The preference values and the entity tag a later save must echo.
     */
    public async Task<SettingsRevision> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.SendAsync<SettingsDto>("Settings", HttpMethod.Get, Path, null, null, cancellationToken);
            return new SettingsRevision(response.Body ?? throw EmptyResponse(), response.ETag ?? throw EmptyResponse());
        }
        catch (ApiError failure)
        {
            throw new ApiError(failure.Kind, ReadMessageFor(failure), failure.Status, failure.Errors);
        }
    }

    /**
     * replaces every preference, but only when the service still holds the revision the caller read.
     * @param settings The complete writable preference set.
     * @param etag The entity tag from the last read or save, sent as If-Match.
     * @param cancellationToken Cancels the pending save.
     * @return The saved values and the new entity tag.
     */
    public async Task<SettingsRevision> SaveAsync(SettingsDto settings, string etag, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.SendAsync<SettingsDto>("Settings", HttpMethod.Put, Path, settings, etag, cancellationToken);
            return new SettingsRevision(response.Body ?? throw EmptyResponse(), response.ETag ?? throw EmptyResponse());
        }
        catch (ApiError failure)
        {
            throw new ApiError(failure.Kind, MessageFor(failure), failure.Status, failure.Errors);
        }
    }

    /**
     * maps a failed save to the guidance the settings view shows, keeping the categorized kind for styling.
     * @param failure The categorized failure from the protected call.
     * @return A user-safe message that never includes provider or storage details.
     */
    static string MessageFor(ApiError failure) => failure.Status switch
    {
        (int)HttpStatusCode.BadRequest => "Check the highlighted settings. Your edits are kept.",
        (int)HttpStatusCode.Forbidden => "Your account or permission does not allow changing settings. Your edits are kept.",
        (int)HttpStatusCode.PreconditionFailed => "These settings changed since you loaded them. Reload the saved settings, review them, then save again.",
        (int)HttpStatusCode.PreconditionRequired => "The settings must be reloaded before saving. Nothing was saved.",
        >= 500 => "Settings are temporarily unavailable. Your edits are kept; try again shortly.",
        null when failure.Kind == FailureKind.Network => "The save could not be confirmed. Reload the saved settings to check before saving again.",
        _ => failure.Message,
    };

    /**
     * maps a failed read to guidance for the settings view.
     * @param failure The categorized failure from the protected call.
     * @return A user-safe message that never includes provider or storage details.
     */
    static string ReadMessageFor(ApiError failure) => failure.Status switch
    {
        (int)HttpStatusCode.Forbidden => "Your account or permission does not allow reading these settings.",
        >= 500 => "Settings are temporarily unavailable. Try again shortly.",
        null when failure.Kind == FailureKind.Network => "Check your connection and try again.",
        _ => failure.Message,
    };

    static ApiError EmptyResponse() => new(FailureKind.Unexpected, "The settings service returned no version of your settings. Try again.");
}
