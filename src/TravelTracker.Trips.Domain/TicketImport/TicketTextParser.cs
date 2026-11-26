using System.Globalization;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Trips.Domain.TicketImport;

/**
 * reads pasted plain ticket text made of "label: value" lines into a proposed draft. Text is
 * never run as markup, nothing is saved, and a value the text does not clearly state is marked
 * missing or ambiguous rather than guessed.
 */
public static class TicketTextParser
{
    /** largest accepted paste, in characters. */
    public const int MaximumCharacters = 4000;

    /** most lines a paste may have; the labelled fields need only a handful. */
    public const int MaximumLines = 60;

    private static readonly string[] LabelNames = ["reference", "mode", "from", "to", "time zone", "departure", "arrival"];
    private static readonly string[] SupportedTransportCodes = ["car", "bus", "train", "pedestrian", "bike", "plane"];
    private static readonly string[] DateTimeFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm"];

    /**
     * parses labelled lines such as "Departure: 2026-06-01 09:00" into proposed fields. Labels are
     * case-insensitive, unlabelled lines are ignored, and a label repeated with different values is ambiguous.
     *
     * @param text the pasted text, as typed or copied.
     * @return the proposed draft, with every field marked known, missing or ambiguous.
     * @throws TripValidationException when the text is blank, too long, has too many lines or contains a NUL character.
     */
    public static TicketImportDraft Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw Reject("paste the ticket text before importing.");
        if (text.Length > MaximumCharacters) throw Reject($"ticket text is longer than {MaximumCharacters} characters.");
        if (text.Contains('\0')) throw Reject("ticket text contains a NUL character.");
        if (text.Split('\n').Length > MaximumLines) throw Reject($"ticket text has more than {MaximumLines} lines.");

        var labelled = CollectLabels(text);
        var timeZone = ZoneId(Single(labelled, "time zone"));
        var departure = Moment(Single(labelled, "departure"), timeZone);
        var arrival = Moment(Single(labelled, "arrival"), timeZone);
        if (departure.Status == ProposedFieldStatus.Known && arrival.Status == ProposedFieldStatus.Known && arrival.Value < departure.Value)
            arrival = ProposedField<DateTimeOffset>.Ambiguous("arrives before it departs; check both times.");

        return new(
            Single(labelled, "reference"),
            TransportCode(Single(labelled, "mode")),
            Single(labelled, "from"),
            Single(labelled, "to"),
            timeZone,
            departure,
            arrival);
    }

    /**
     * groups the values of the known labels, keeping every occurrence so repeats can be detected.
     *
     * @param text the pasted text.
     * @return each known label mapped to its values in the order they appear.
     */
    private static Dictionary<string, List<string>> CollectLabels(string text)
    {
        var labelled = LabelNames.ToDictionary(name => name, _ => new List<string>());
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var label = line[..separator].Trim().ToLowerInvariant();
            if (labelled.TryGetValue(label, out var values)) values.Add(line[(separator + 1)..].Trim());
        }
        return labelled;
    }

    /**
     * reads one text label. A repeated label with different values is ambiguous rather than picked.
     *
     * @param labelled the values collected per label.
     * @param label the known label to read.
     * @return the proposed text field.
     */
    private static ProposedField<string> Single(Dictionary<string, List<string>> labelled, string label)
    {
        var distinct = labelled[label].Distinct().ToList();
        if (distinct.Count == 0) return ProposedField<string>.Missing($"no \"{label}\" line.");
        if (distinct.Count > 1) return ProposedField<string>.Ambiguous($"\"{label}\" appears with different values.");
        return distinct[0].Length == 0 ? ProposedField<string>.Missing($"\"{label}\" has no value.") : ProposedField<string>.Known(distinct[0]);
    }

    /**
     * checks the transport mode against the supported codes.
     *
     * @param raw the mode as read from the text.
     * @return the lowercase code when supported, otherwise an ambiguous field.
     */
    private static ProposedField<string> TransportCode(ProposedField<string> raw)
    {
        if (raw.Status != ProposedFieldStatus.Known) return raw;
        var code = raw.Value!.ToLowerInvariant();
        return SupportedTransportCodes.Contains(code)
            ? ProposedField<string>.Known(code)
            : ProposedField<string>.Ambiguous($"\"{raw.Value}\" is not a supported transport mode.");
    }

    /**
     * checks the time zone name against the zones this machine knows.
     *
     * @param raw the zone name as read from the text.
     * @return the zone id when it exists, otherwise an ambiguous field.
     */
    private static ProposedField<string> ZoneId(ProposedField<string> raw)
    {
        if (raw.Status != ProposedFieldStatus.Known) return raw;
        try
        {
            return ProposedField<string>.Known(TimeZoneInfo.FindSystemTimeZoneById(raw.Value!).Id);
        }
        catch (TimeZoneNotFoundException)
        {
            return ProposedField<string>.Ambiguous($"\"{raw.Value}\" is not a known time zone.");
        }
        catch (InvalidTimeZoneException)
        {
            return ProposedField<string>.Ambiguous($"\"{raw.Value}\" is not a known time zone.");
        }
    }

    /**
     * turns a typed wall-clock time into an absolute time, through the same rules trips use. Skipped
     * and repeated clock times stay ambiguous, because the offset is the traveller's choice.
     *
     * @param raw the time as read from the text.
     * @param zoneField the proposed time zone the time is written in.
     * @return the resolved moment, or a missing or ambiguous field explaining why not.
     */
    private static ProposedField<DateTimeOffset> Moment(ProposedField<string> raw, ProposedField<string> zoneField)
    {
        if (raw.Status != ProposedFieldStatus.Known) return new(raw.Status, default, raw.Reason);
        if (!DateTime.TryParseExact(raw.Value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return ProposedField<DateTimeOffset>.Ambiguous($"\"{raw.Value}\" is not a date and time such as 2026-06-01 09:00.");
        if (zoneField.Status != ProposedFieldStatus.Known)
            return ProposedField<DateTimeOffset>.Ambiguous("the time zone needs review before this time can be placed.");
        try
        {
            return ProposedField<DateTimeOffset>.Known(TripTime.Resolve(local, TimeZoneInfo.FindSystemTimeZoneById(zoneField.Value!)));
        }
        catch (TripValidationException exception)
        {
            return ProposedField<DateTimeOffset>.Ambiguous(exception.Errors[0].Message);
        }
    }

    /**
     * @param message what is wrong with the pasted text.
     * @return the exception naming the ticket text field.
     */
    private static TripValidationException Reject(string message) => new([new("ticketText", message)]);
}
