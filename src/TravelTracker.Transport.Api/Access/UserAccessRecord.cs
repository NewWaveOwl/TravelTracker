namespace TravelTracker.Transport.Api.Access;

/** Describes live Users standing and role for the validated caller. */
public sealed record UserAccessRecord(string Status, string Role);
