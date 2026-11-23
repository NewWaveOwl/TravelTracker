namespace TravelTracker.Frontend.Features.Trips;

/** Resolves route departures without guessing daylight-saving offsets or changing a saved trip zone. */
public static class RouteLocalTime
{
    /**
     * Normalizes a supported Windows zone for the shared router's IANA contract.
     * @param zoneId The unchanged trip time-zone identifier.
     * @return An equivalent IANA identifier, or null when the zone is unsupported.
     */
    public static string? RouterZone(string zoneId)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId.Trim(), out var zone)) return null;
        if (zone.Id == "UTC") return "Etc/UTC";
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana)) return iana;
        return zone.HasIanaId ? zone.Id : null;
    }

    /**
     * Lists the valid offsets for a local departure, with no options during a daylight-saving gap.
     * @param local The traveller's local date and time.
     * @param zoneId The trip zone.
     * @return Zero, one or two valid offsets.
     */
    public static TimeSpan[] Offsets(DateTime? local, string zoneId)
    {
        if (local is not { } value || !TimeZoneInfo.TryFindSystemTimeZoneById(zoneId.Trim(), out var zone)) return [];
        value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(value)) return [];
        return zone.IsAmbiguousTime(value) ? zone.GetAmbiguousTimeOffsets(value).OrderDescending().ToArray() : [zone.GetUtcOffset(value)];
    }

    /**
     * Creates an offset-aware departure only after an ambiguous local time has been explicitly resolved.
     * @param local The entered local date and time.
     * @param zoneId The trip zone.
     * @param choice A chosen offset for a repeated local time.
     * @return The exact departure, or null while its date, zone or offset is unresolved.
     */
    public static DateTimeOffset? Resolve(DateTime? local, string zoneId, TimeSpan? choice)
    {
        var options = Offsets(local, zoneId);
        if (local is not { } value || options.Length == 0 || options.Length > 1 && (choice is null || !options.Contains(choice.Value))) return null;
        var offset = options.Length == 1 ? options[0] : choice!.Value;
        value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var utcTicks = value.Ticks - offset.Ticks;
        return utcTicks >= DateTime.MinValue.Ticks && utcTicks <= DateTime.MaxValue.Ticks ? new DateTimeOffset(value, offset) : null;
    }
}
