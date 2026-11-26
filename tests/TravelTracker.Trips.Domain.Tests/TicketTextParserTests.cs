using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Domain.TicketImport;

namespace TravelTracker.Trips.Domain.Tests;

/**
 * Verifies that pasted ticket text becomes proposed fields without guessing; no host, no storage.
 */
public sealed class TicketTextParserTests
{
    /**
     * joins lines the way a paste arrives.
     *
     * @param lines the labelled lines to paste.
     * @return the pasted text.
     */
    private static string Paste(params string[] lines) => string.Join('\n', lines);

    /**
     * Accepts a complete labelled ticket and resolves its times with the stated zone.
     */
    [Fact]
    public void Parse_CompleteTicket_AllFieldsKnown()
    {
        var draft = TicketTextParser.Parse(Paste(
            "Reference: ABC123",
            "Mode: Train",
            "From: Gare de Lyon",
            "To: Nice Ville",
            "Time zone: Europe/Paris",
            "Departure: 2026-06-01 09:00",
            "Arrival: 2026-06-01 12:30"));

        Assert.Equal("ABC123", draft.BookingReference.Value);
        Assert.Equal("train", draft.TransportCode.Value);
        Assert.Equal("Gare de Lyon", draft.FromPlaceText.Value);
        Assert.Equal("Nice Ville", draft.ToPlaceText.Value);
        Assert.Equal("Europe/Paris", draft.TimeZoneId.Value);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.FromHours(2)), draft.Departure.Value);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.FromHours(2)), draft.Arrival.Value);
        Assert.All(new[] { draft.BookingReference.Status, draft.TransportCode.Status, draft.FromPlaceText.Status, draft.ToPlaceText.Status,
            draft.TimeZoneId.Status, draft.Departure.Status, draft.Arrival.Status }, status => Assert.Equal(ProposedFieldStatus.Known, status));
    }

    /**
     * Leaves every field the ticket does not state as missing, with no invented location or time.
     */
    [Fact]
    public void Parse_OnlyModeAndFrom_OtherFieldsMissingWithoutInvention()
    {
        var draft = TicketTextParser.Parse(Paste("Mode: bus", "From: Central Station"));

        Assert.Equal(ProposedFieldStatus.Known, draft.TransportCode.Status);
        Assert.Equal(ProposedFieldStatus.Known, draft.FromPlaceText.Status);
        Assert.Equal(ProposedFieldStatus.Missing, draft.ToPlaceText.Status);
        Assert.Null(draft.ToPlaceText.Value);
        Assert.Equal(ProposedFieldStatus.Missing, draft.Departure.Status);
        Assert.Equal(default, draft.Departure.Value);
        Assert.Equal(ProposedFieldStatus.Missing, draft.Arrival.Status);
        Assert.Equal(ProposedFieldStatus.Missing, draft.TimeZoneId.Status);
    }

    /**
     * Keeps a time ambiguous when its zone is missing, since it cannot be placed without one.
     */
    [Fact]
    public void Parse_TimesWithoutZone_TimesAmbiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Departure: 2026-06-01 09:00", "Arrival: 2026-06-01 10:00"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Departure.Status);
        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Arrival.Status);
        Assert.Equal(default, draft.Departure.Value);
    }

    /**
     * Marks a label repeated with different values as ambiguous rather than choosing one.
     */
    [Fact]
    public void Parse_RepeatedLabelWithDifferentValues_Ambiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Reference: ABC123", "Reference: XYZ999"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.BookingReference.Status);
        Assert.Null(draft.BookingReference.Value);
    }

    /**
     * Accepts a label repeated with the same value, because nothing is in conflict.
     */
    [Fact]
    public void Parse_RepeatedLabelWithSameValue_Known()
    {
        var draft = TicketTextParser.Parse(Paste("Reference: ABC123", "reference: ABC123"));

        Assert.Equal(ProposedFieldStatus.Known, draft.BookingReference.Status);
        Assert.Equal("ABC123", draft.BookingReference.Value);
    }

    /**
     * Flags a transport mode outside the supported codes instead of mapping it to something close.
     */
    [Fact]
    public void Parse_UnsupportedMode_Ambiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Mode: ferry"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.TransportCode.Status);
        Assert.Null(draft.TransportCode.Value);
    }

    /**
     * Treats a label with no value as missing, not as an empty known value.
     */
    [Fact]
    public void Parse_LabelWithoutValue_Missing()
    {
        var draft = TicketTextParser.Parse(Paste("Reference:   ", "From: "));

        Assert.Equal(ProposedFieldStatus.Missing, draft.BookingReference.Status);
        Assert.Equal(ProposedFieldStatus.Missing, draft.FromPlaceText.Status);
    }

    /**
     * Rejects a date or time that is not in the supported format, leaving the field ambiguous.
     *
     * @param departure the malformed departure line value.
     */
    [Theory]
    [InlineData("2026-06-01")]
    [InlineData("2026-13-01 09:00")]
    [InlineData("tomorrow 9am")]
    [InlineData("2026-06-01 25:00")]
    public void Parse_MalformedDeparture_Ambiguous(string departure)
    {
        var draft = TicketTextParser.Parse(Paste("Time zone: Europe/Paris", $"Departure: {departure}"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Departure.Status);
        Assert.Equal(default, draft.Departure.Value);
    }

    /**
     * Rejects a zone name this machine does not know, and leaves times in that zone ambiguous.
     */
    [Fact]
    public void Parse_UnknownZone_ZoneAndTimesAmbiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Time zone: Mars/Olympus", "Departure: 2026-06-01 09:00"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.TimeZoneId.Status);
        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Departure.Status);
    }

    /**
     * Keeps a time that falls in a spring-forward gap ambiguous, because the clock never shows it.
     */
    [Fact]
    public void Parse_TimeSkippedByClockChange_Ambiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Time zone: Europe/Paris", "Departure: 2026-03-29 02:30"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Departure.Status);
    }

    /**
     * Keeps a time that falls in a repeated fall-back hour ambiguous, because the offset is the traveller's choice.
     */
    [Fact]
    public void Parse_TimeRepeatedByClockChange_Ambiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Time zone: Europe/Paris", "Departure: 2026-10-25 02:30"));

        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Departure.Status);
    }

    /**
     * Flags an arrival before the departure rather than proposing it as a leg.
     */
    [Fact]
    public void Parse_ArrivalBeforeDeparture_ArrivalAmbiguous()
    {
        var draft = TicketTextParser.Parse(Paste("Time zone: Europe/Paris", "Departure: 2026-06-01 12:00", "Arrival: 2026-06-01 09:00"));

        Assert.Equal(ProposedFieldStatus.Known, draft.Departure.Status);
        Assert.Equal(ProposedFieldStatus.Ambiguous, draft.Arrival.Status);
    }

    /**
     * Stores markup as plain text, so a pasted script or image tag is only ever a string value.
     */
    [Fact]
    public void Parse_MarkupInValues_StoredAsPlainText()
    {
        const string markup = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        var draft = TicketTextParser.Parse(Paste($"From: {markup}", "Reference: <b>ABC</b>"));

        Assert.Equal(markup, draft.FromPlaceText.Value);
        Assert.Equal("<b>ABC</b>", draft.BookingReference.Value);
    }

    /**
     * Matches labels regardless of case and ignores lines without a known label; values keep their own colons.
     */
    [Fact]
    public void Parse_MixedCaseAndUnlabelledLines_ReadsKnownLabelsOnly()
    {
        var draft = TicketTextParser.Parse(Paste("Your booking is confirmed!", "REFERENCE: AB:12", "random note: nothing", "mOdE: Plane"));

        Assert.Equal("AB:12", draft.BookingReference.Value);
        Assert.Equal("plane", draft.TransportCode.Value);
    }

    /**
     * Rejects input that is blank, oversized, has too many lines or contains a NUL character, and names the text field.
     *
     * @param text the pasted text that must be refused.
     */
    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("Mode: train\0")]
    public void Parse_BlankOrNulText_Rejected(string text)
    {
        var exception = Assert.Throws<TripValidationException>(() => TicketTextParser.Parse(text));

        Assert.Equal("ticketText", Assert.Single(exception.Errors).Field);
    }

    /**
     * Refuses a paste one character over the bound and accepts one exactly at it.
     */
    [Fact]
    public void Parse_OversizedText_RejectedAndBoundaryAccepted()
    {
        var atBound = "From: " + new string('a', TicketTextParser.MaximumCharacters - "From: ".Length);

        Assert.Equal(TicketTextParser.MaximumCharacters, atBound.Length);
        Assert.Equal(TicketTextParser.MaximumCharacters - "From: ".Length, TicketTextParser.Parse(atBound).FromPlaceText.Value!.Length);
        Assert.Throws<TripValidationException>(() => TicketTextParser.Parse(atBound + "a"));
    }

    /**
     * Refuses more lines than the bound, even when every line is short.
     */
    [Fact]
    public void Parse_TooManyLines_Rejected()
    {
        var lines = Enumerable.Repeat("Note: x", TicketTextParser.MaximumLines + 1).ToArray();

        var exception = Assert.Throws<TripValidationException>(() => TicketTextParser.Parse(Paste(lines)));

        Assert.Equal("ticketText", Assert.Single(exception.Errors).Field);
    }
}
