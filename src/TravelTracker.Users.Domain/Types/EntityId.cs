namespace TravelTracker.Users.Domain.Types;

/**
 * Pairs a persistence-only key with the public UUID used outside Data.
 */
public readonly record struct EntityId<TEntity>
{
    internal Id64 Key { get; }
    public Guid PublicId { get; }

    internal EntityId(Id64 key, Guid publicId)
    {
        Key = key;
        PublicId = publicId;
    }

    /**
     * Formats the public identifier without exposing the internal key.
     * @return The public UUID string.
     */
    public override string ToString() => PublicId.ToString();
}
