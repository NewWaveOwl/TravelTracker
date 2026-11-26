namespace TravelTracker.Trips.Domain.TicketImport;

/**
 * the fields proposed from one pasted ticket. Nothing here is saved; the traveller reviews it first.
 *
 * @param BookingReference booking or confirmation reference as printed on the ticket.
 * @param TransportCode supported transport code, such as train or plane.
 * @param FromPlaceText departure place as written on the ticket, not yet matched to a saved place.
 * @param ToPlaceText arrival place as written on the ticket, not yet matched to a saved place.
 * @param TimeZoneId IANA time zone the ticket times are written in.
 * @param Departure departure moment, resolved with the time zone.
 * @param Arrival arrival moment, resolved with the time zone.
 */
public sealed record TicketImportDraft(
    ProposedField<string> BookingReference,
    ProposedField<string> TransportCode,
    ProposedField<string> FromPlaceText,
    ProposedField<string> ToPlaceText,
    ProposedField<string> TimeZoneId,
    ProposedField<DateTimeOffset> Departure,
    ProposedField<DateTimeOffset> Arrival);
