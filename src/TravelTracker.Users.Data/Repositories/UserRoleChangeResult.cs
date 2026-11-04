using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Data.Repositories;

/**
 * Describes an atomic role-change outcome without exposing persistence details.
 * @param Code changed, forbidden, notfound, conflict, last-administrator or invalid.
 * @param Account Confirmed saved account for a changed outcome; otherwise null.
 */
public sealed record UserRoleChangeResult(string Code, UserAccount? Account);
