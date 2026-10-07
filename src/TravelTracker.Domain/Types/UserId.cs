namespace TravelTracker.Domain.Types;

/**
 * One user identity with two faces: the Id64 used only for database
 * queries, and the uuidv7 that is freely shown to clients. Key is
 * internal by design (visible to Data only), so it never reaches
 * json, logs, or other layers; PublicId is the only thing users see.
 */
public readonly record struct UserId {
    /** The secret database identifier. Visible to Data via InternalsVisibleTo. */
    internal Id64 Key { get; }

    /** The external uuidv7, safe to expose. */
    public Guid PublicId { get; }

    internal UserId(Id64 key, Guid publicId) {
        Key = key;
        PublicId = publicId;
    }

    /** A new user identity; the generator keeps Id64 out of callers' hands. */
    public static async ValueTask<UserId> NewAsync(Id64Generator generator) => new(await generator.NextAsync(), Guid.CreateVersion7());

    public override string ToString() => PublicId.ToString();
}
