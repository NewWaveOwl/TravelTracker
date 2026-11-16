namespace TravelTracker.Users.Domain.Types;

/**
 * Internal numeric key used only for persistence joins and lookup.
 */
internal readonly record struct Id64
{
    private readonly long value;

    /** Wraps a raw key. */
    public Id64(long value) => this.value = value;

    /** Unwraps the raw key for Data-layer queries. */
    public static explicit operator long(Id64 id) => id.value;

    /** Formats the raw key for diagnostics inside the service; clients never see it. */
    public override string ToString() => value.ToString();
}
