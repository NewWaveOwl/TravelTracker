namespace TravelTracker.Api.References;

/**
 * Result of one upstream lookup. The value is present only when the status is Found.
 *
 * @param Status Outcome category.
 * @param Value The found value, or default when the lookup did not find a record.
 * @param Detail Safe diagnostic text; never URLs, tokens, response bodies or identities.
 * @typeparam TValue The type of the found value.
 */
public sealed record ReferenceResult<TValue>(ReferenceStatus Status, TValue? Value, string Detail)
{
    /**
     * Wraps a found value.
     * @param value The value the upstream service returned.
     * @return A Found result.
     */
    public static ReferenceResult<TValue> Found(TValue value) => new(ReferenceStatus.Found, value, "found");

    /**
     * Builds a result that carries no value.
     * @param status The non-found category.
     * @param detail Safe diagnostic text.
     * @return A result without a value.
     */
    public static ReferenceResult<TValue> Failed(ReferenceStatus status, string detail) =>
        status == ReferenceStatus.Found
            ? throw new ArgumentException("A Found result must carry a value; use Found.", nameof(status))
            : new(status, default, detail);

    /**
     * Carries a non-found outcome into a result of another value type.
     * @typeparam TOther The value type of the target result.
     * @return The same status and detail with no value.
     * @throws InvalidOperationException when Status is Found, because the value would be lost.
     */
    public ReferenceResult<TOther> Forward<TOther>() =>
        Status == ReferenceStatus.Found
            ? throw new InvalidOperationException("A Found result carries a value and cannot be forwarded.")
            : new(Status, default, Detail);
}
