using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Api.Services;

/**
 * the outcome of a replace request: saved values, field errors that rejected them, or a stale revision.
 * @param Saved the stored values with their new revision when the request was accepted.
 * @param Errors field errors keyed by JSON field name when the candidate was invalid.
 * @param Stale true when the caller's If-Match revision no longer matches the stored one, so nothing was written.
 */
public sealed record SettingsReplaceResult(UserSettings? Saved, Dictionary<string, string[]>? Errors, bool Stale = false);
