namespace TravelTracker.Attractions.Domain.Types;

/**
 * Internal join key: a monotonically increasing long issued by the Data
 * layer's generator. Starts at 1 and may wrap into negatives; default (0) is never issued.
 * Internal on purpose: only Domain and Data can see it, so the Api can
 * neither name nor expose it. The public face of an entity is its Guid.
 */
internal readonly record struct Id64
{
    private readonly long value;

    /**
     * Wraps a raw key.
     * @param value The raw database key.
     */
    public Id64(long value) => this.value = value;

    /**
     * Unwraps the raw key for Data-layer queries.
     * @param id The key to unwrap.
     * @return The raw database key.
     */
    public static explicit operator long(Id64 id) => id.value;

    /**
     * Formats the raw key for diagnostics inside the service; clients never see it.
     * @return The decimal text of the raw key.
     */
    public override string ToString() => value.ToString();
}
