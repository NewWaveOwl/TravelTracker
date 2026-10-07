namespace TravelTracker.Domain.Types;

/**
 * Internal join key: a monotonically increasing long issued by
 * Id64Generator, not the database. Starts at 1, so default (0) means
 * never issued. Never exposed to clients; the public face of an entity is
 * its Guid.
 */
public readonly record struct Id64 {
    private readonly long value;

    public Id64(long value) => this.value = value;

    /** True once the generator has issued this id. */
    public bool IsAssigned => value > 0;

    public static explicit operator long(Id64 id) => id.value;

    public override string ToString() => value.ToString();
}
