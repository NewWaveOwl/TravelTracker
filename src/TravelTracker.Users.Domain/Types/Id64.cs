namespace TravelTracker.Users.Domain.Types;

/**
 * Internal numeric key used only for persistence joins and lookup.
 */
internal readonly record struct Id64
{
    private readonly long value;

    public Id64(long value) => this.value = value;

    public static explicit operator long(Id64 id) => id.value;

    public override string ToString() => value.ToString();
}
