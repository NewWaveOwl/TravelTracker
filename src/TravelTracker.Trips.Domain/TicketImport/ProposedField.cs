namespace TravelTracker.Trips.Domain.TicketImport;

/**
 * one extracted ticket value with the reason it is not usable when it is not known.
 *
 * @param Status whether the value is known, missing or ambiguous.
 * @param Value the proposed value; null unless the status is known.
 * @param Reason what the traveller needs to check; null when the value is known.
 */
public sealed record ProposedField<T>(ProposedFieldStatus Status, T? Value, string? Reason)
{
    /**
     * @param value the value the ticket states.
     * @return a known field.
     */
    public static ProposedField<T> Known(T value) => new(ProposedFieldStatus.Known, value, null);

    /**
     * @param reason why the ticket does not state the value.
     * @return a missing field.
     */
    public static ProposedField<T> Missing(string reason) => new(ProposedFieldStatus.Missing, default, reason);

    /**
     * @param reason why the stated value cannot be used as it stands.
     * @return an ambiguous field.
     */
    public static ProposedField<T> Ambiguous(string reason) => new(ProposedFieldStatus.Ambiguous, default, reason);
}
