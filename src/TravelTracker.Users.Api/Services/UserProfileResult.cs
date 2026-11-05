using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Api.Services;

/**
 * Carries a self-profile operation outcome to the HTTP boundary.
 * @param Kind The application outcome to map to HTTP.
 * @param Account The caller's account for a successful operation, or null otherwise.
 * @param ValidationErrors Field errors for an invalid update, or null for other outcomes.
 */
public sealed record UserProfileResult(
    UserProfileResultKind Kind,
    UserAccount? Account = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
