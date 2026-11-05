using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Data.Repositories;

/**
 * Describes an own-profile update outcome without exposing persistence details.
 * @param Code updated, notfound, forbidden or invalid.
 * @param Account Confirmed saved account for an updated outcome; otherwise null.
 */
public sealed record UserProfileUpdateResult(string Code, UserAccount? Account);
