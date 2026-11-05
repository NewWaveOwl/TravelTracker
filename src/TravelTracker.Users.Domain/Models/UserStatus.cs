namespace TravelTracker.Users.Domain.Models;

/**
 * Identifies whether a Users account is active, deactivated or banned.
 */
public enum UserStatus
{
    Active = 0,
    Deactivated = 1,
    Banned = 2
}
