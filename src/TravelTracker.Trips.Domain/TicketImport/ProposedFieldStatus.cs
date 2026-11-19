namespace TravelTracker.Trips.Domain.TicketImport;

/**
 * whether an extracted ticket value can be proposed to the traveller as it stands.
 */
public enum ProposedFieldStatus
{
    /** the ticket states a usable value. */
    Known,

    /** the ticket does not state this value at all. */
    Missing,

    /** the ticket states something, but it cannot be used without the traveller checking it. */
    Ambiguous
}
