namespace TravelTracker.Frontend.Features.Trips;

/** Resolves the first or last instant of a selected calendar day in the trip time zone. */
internal static class TripDayBoundary
{
    /**
     * @param day The selected local calendar date.
     * @param zone The trip's time zone.
     * @param end Whether to use the last instant of the day.
     * @return The inclusive boundary, or null when the date cannot be represented.
     */
    internal static DateTimeOffset? Resolve(DateOnly day, TimeZoneInfo zone, bool end)
    {
        var local = day.ToDateTime(end ? TimeOnly.MaxValue : TimeOnly.MinValue);
        while (zone.IsInvalidTime(local))
        {
            if (end ? local.Hour == 0 && local.Minute == 0 : local.Hour == 23 && local.Minute == 59) return null;
            local = local.AddMinutes(end ? -1 : 1);
        }
        var offset = zone.IsAmbiguousTime(local)
            ? (end ? zone.GetAmbiguousTimeOffsets(local).Min() : zone.GetAmbiguousTimeOffsets(local).Max())
            : zone.GetUtcOffset(local);
        var utcTicks = local.Ticks - offset.Ticks;
        return utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks ? null : new DateTimeOffset(local, offset);
    }
}
