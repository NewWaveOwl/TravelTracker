using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Entities;

/** Stores the normalized picture separately from ordinary user-profile queries. */
internal sealed class UserPictureRow
{
    public Id64 AccountId { get; set; }
    public byte[] Content { get; set; } = [];
}
