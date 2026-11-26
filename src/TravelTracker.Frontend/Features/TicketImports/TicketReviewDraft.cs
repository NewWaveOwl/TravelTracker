using System.Globalization;
using TravelTracker.Frontend.Api.TicketImports;

namespace TravelTracker.Frontend.Features.TicketImports;

/** Editable, memory-only proposed values; this model never saves or reconnects an itinerary. */
public sealed class TicketReviewDraft
{
    public TicketDraftField[] Fields { get; } =
    [
        new("ticketType", "Ticket type", true), new("traveler", "Traveler", false),
        new("operator", "Operator", true), new("bookingReference", "Booking reference", false),
        new("from", "From", true), new("to", "To", true),
        new("departureDate", "Departure date", true, "date"), new("departureTime", "Departure time", true, "time"),
        new("arrivalDate", "Arrival date", true, "date"), new("arrivalTime", "Arrival time", true, "time"),
        new("timeZone", "Time zone", true), new("seat", "Seat (optional)", false)
    ];

    /**
     * Copies only supported proposed values; absent or conflicting source fields stay visibly uncertain.
     * @param source Public extraction response, or null for manual entry.
     * @return A fresh editable review with no default travel values.
     */
    public static TicketReviewDraft From(TicketImportDto? source)
        => FromFields(source?.Fields);

    public static TicketReviewDraft FromFields(TicketFieldDto[]? fields)
    {
        var draft = new TicketReviewDraft();
        foreach (var field in draft.Fields)
        {
            var matches = fields?.Where(candidate => candidate.Name == field.Name).ToArray();
            if (matches is not { Length: 1 }) continue;
            var found = matches[0];
            field.Value = found.Value is { Length: <= 500 } ? found.Value : "";
            field.Status = found.Status is "extracted" or "uncertain" or "missing" ? found.Status : "uncertain";
        }
        return draft;
    }

    /**
     * Reads a reviewed field for the summary, preserving explicitly empty values.
     * @param name Supported field name.
     * @return The current editable value.
     */
    public string Value(string name) => Fields.Single(field => field.Name == name).Value;

    /**
     * Checks required values and unambiguous date/time syntax before marking this draft reviewed.
     * @return True when the traveller has supplied every required value without changing a saved trip.
     */
    public bool Validate()
    {
        foreach (var field in Fields)
        {
            field.Error = field.Required && string.IsNullOrWhiteSpace(field.Value) ? $"Enter {field.Label.ToLowerInvariant()}." : null;
            if (field.Value.Length > 500) field.Error = "Use 500 characters or fewer.";
            if (field.Value.Length == 0) continue;
            if (field.InputType == "date" && !DateOnly.TryParseExact(field.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                field.Error = "Enter a valid date.";
            if (field.InputType == "time" && !TimeOnly.TryParseExact(field.Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                field.Error = "Enter a valid time.";
        }
        var zone = Fields.Single(field => field.Name == "timeZone");
        if (!string.IsNullOrWhiteSpace(zone.Value))
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone.Value); }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            { zone.Error = "Enter a recognized time zone, for example America/Toronto."; }
        }
        return Fields.All(field => field.Error is null);
    }
}
