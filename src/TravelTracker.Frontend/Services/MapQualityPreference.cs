using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Services;

/**
 * remembers the map quality choice in this browser. the choice is per device because the automatic default depends on device capabilities, so it is not part of the account settings contract.
 * @param storage Protected browser local storage, the same store the saved map draft uses.
 * @param logger Records failed reads and writes; reads fall back to automatic.
 */
public sealed class MapQualityPreference(ProtectedLocalStorage storage, ILogger<MapQualityPreference> logger)
{
    const string Key = "traveltracker-map-quality-v1";

    /**
     * reads the saved choice for this browser.
     * @return The saved choice, or automatic when none is saved or the read fails; failures are logged.
     */
    public async Task<string> GetAsync()
    {
        try
        {
            var result = await storage.GetAsync<string>(Key);
            return result.Success && result.Value is { } saved && MapQuality.Choices.Contains(saved) ? saved : MapQuality.Automatic;
        }
        catch (Exception exception) when (exception is JSDisconnectedException)
        {
            logger.LogDebug("Map quality not read because the browser connection closed");
            return MapQuality.Automatic;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JSException or CryptographicException)
        {
            logger.LogWarning(exception, "Map quality could not be read from this browser; automatic applies");
            return MapQuality.Automatic;
        }
    }

    /**
     * saves the choice in this browser.
     * @param choice Automatic, standard or low.
     * @return True when the choice was stored; false when the browser refused it, which is logged.
     */
    public async Task<bool> SetAsync(string choice)
    {
        if (!MapQuality.Choices.Contains(choice)) throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unknown map quality.");
        try
        {
            await storage.SetAsync(Key, choice);
            return true;
        }
        catch (Exception exception) when (exception is JSDisconnectedException or InvalidOperationException or JSException)
        {
            logger.LogWarning(exception, "Map quality could not be saved in this browser");
            return false;
        }
    }
}
