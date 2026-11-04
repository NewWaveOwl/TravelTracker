namespace TravelTracker.Attractions.Domain.Types;

/**
 * One entity identity with two faces: the Id64 used only for database
 * queries and joins, and the uuidv7 that is freely shown to clients. Key is
 * internal by design (visible to Data only), so it never reaches json, logs,
 * or the Api; PublicId is the only thing users see. The type parameter keeps
 * ids of different entities from being mixed up.
 *
 * Only Data can create one (through its generator or from a stored row); the
 * default value means "no identity yet".
 */
public readonly record struct EntityId<TEntity> {
    /**
     * The secret database identifier. Visible to Data via InternalsVisibleTo.
     */
    internal Id64 Key { get; }

    /**

     * The external uuidv7, safe to expose.

     */
    public Guid PublicId { get; }

    internal EntityId(Id64 key, Guid publicId) {
        Key = key;
        PublicId = publicId;
    }

    public override string ToString() => PublicId.ToString();
}
