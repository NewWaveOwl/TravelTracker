namespace TravelTracker.Accommodations.Api.Access;

/** Describes the live Users standing and role resolved for one validated identity. */
public sealed record UserAccessRecord(string Status, string Role);
