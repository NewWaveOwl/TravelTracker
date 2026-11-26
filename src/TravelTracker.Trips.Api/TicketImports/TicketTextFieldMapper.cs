using System.Globalization;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Domain.TicketImport;

namespace TravelTracker.Trips.Api.TicketImports;

/**
 * Converts a pasted-text draft into the PDF import's named fields so review screens treat both sources alike.
 * Only the labels the text format reads are returned; traveller, operator and seat are not read from pasted text.
 */
public static class TicketTextFieldMapper
{
    /**
     * @param draft Proposed fields read from pasted text.
     * @return Named fields using the PDF import's status words; departure and arrival are split in the ticket's own zone.
     */
    public static IReadOnlyList<TicketImportFieldResponse> Map(TicketImportDraft draft) =>
        [
            Text("ticketType", draft.TransportCode),
            Text("bookingReference", draft.BookingReference),
            Text("from", draft.FromPlaceText),
            Text("to", draft.ToPlaceText),
            .. Moment("departureDate", "departureTime", draft.Departure, draft.TimeZoneId),
            .. Moment("arrivalDate", "arrivalTime", draft.Arrival, draft.TimeZoneId),
            Text("timeZone", draft.TimeZoneId)
        ];

    /**
     * @param name Supported ticket field name.
     * @param field Proposed text value.
     * @return The named field with its status word.
     */
    private static TicketImportFieldResponse Text(string name, ProposedField<string> field) => new(name, field.Value, StatusWord(field.Status));

    /**
     * Splits one resolved moment into the date and clock fields the PDF import returns. A known moment always has a known zone.
     * @param dateName Date field name.
     * @param timeName Clock field name.
     * @param moment Resolved moment, or its missing or ambiguous reason.
     * @param zone Proposed zone the moment is shown in.
     * @return Date and time fields, both carrying the moment's status.
     */
    private static TicketImportFieldResponse[] Moment(string dateName, string timeName, ProposedField<DateTimeOffset> moment,
        ProposedField<string> zone)
    {
        var local = moment.Status == ProposedFieldStatus.Known
            ? TimeZoneInfo.ConvertTime(moment.Value, TimeZoneInfo.FindSystemTimeZoneById(zone.Value!))
            : (DateTimeOffset?)null;
        return
        [
            new(dateName, local?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StatusWord(moment.Status)),
            new(timeName, local?.ToString("HH:mm", CultureInfo.InvariantCulture), StatusWord(moment.Status))
        ];
    }

    /**
     * @param status Domain status of one proposed value.
     * @return The PDF import's status word for that status.
     */
    private static string StatusWord(ProposedFieldStatus status) => status switch
    {
        ProposedFieldStatus.Known => "extracted",
        ProposedFieldStatus.Missing => "missing",
        _ => "uncertain"
    };
}
