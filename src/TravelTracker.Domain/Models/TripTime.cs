namespace TravelTracker.Domain.Models;

/**
 * Turns a time typed in a trip's time zone into an absolute time without guessing.
 */
public static class TripTime {
    /**
     * Resolves a local wall-clock time. Impossible times (skipped by a clock change) are
     * rejected, and ambiguous times (repeated by one) need the caller to pick an offset.
     *
     * @param local The wall-clock time as typed.
     * @param zone The trip time zone.
     * @param chosenOffset The offset the traveller picked, required when the time is ambiguous; if given otherwise it must match the zone.
     * @return The absolute time.
     * @throws TripValidationException when the time is impossible, ambiguous without an offset, or the offset does not fit.
     */
    public static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone, TimeSpan? chosenOffset = null) {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            throw Reject($"{local:s} does not exist in {zone.Id} (clocks skip it); choose another time.");
        if (!zone.IsAmbiguousTime(local)) {
            var actual = zone.GetUtcOffset(local);
            return chosenOffset is { } given && given != actual ? throw Reject($"offset {given} is not valid for {local:s} in {zone.Id}; it is {actual}.") : new(local, actual);
        }
        var offsets = zone.GetAmbiguousTimeOffsets(local);
        if (chosenOffset is not { } offset)
            throw Reject($"{local:s} happens twice in {zone.Id}; choose one of the offsets {string.Join(", ", offsets)}.");
        if (!offsets.Contains(offset)) throw Reject($"offset {offset} is not valid for {local:s} in {zone.Id}.");
        return new(local, offset);
    }

    private static TripValidationException Reject(string message) => new([new("time", message)]);
}
