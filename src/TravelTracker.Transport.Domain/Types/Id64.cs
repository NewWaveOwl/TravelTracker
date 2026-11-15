namespace TravelTracker.Transport.Domain.Types;

/**
 * Internal join key: a monotonically increasing long issued by the Data
 * layer's generator. Starts at 1 and may wrap into negatives; default (0) is never issued.
 * Internal on purpose: only Domain and Data can see it, so the Api can
 * neither name nor expose it. The public face of an entity is its Guid.
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
