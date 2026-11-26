namespace TravelTracker.Trips.Domain.Models;

/** Bounds the size and validates the coordinates of a saved polyline5 route. */
public static class TripPath
{
    /** Maximum ASCII bytes per encoded trip leg. */
    public const int MaxEncodedLength = 200_000;

    /**
     * Checks complete latitude/longitude pairs without allocating a decoded copy.
     * @param encoded Provider geometry in five-decimal encoded polyline format.
     * @return True when the path contains at least two valid coordinates.
     */
    public static bool IsValid(string encoded)
    {
        if (encoded.Length > MaxEncodedLength) return false;
        int index = 0, points = 0;
        long latitude = 0, longitude = 0;
        while (index < encoded.Length)
        {
            if (!ReadDelta(encoded, ref index, out var lat) || !ReadDelta(encoded, ref index, out var lon)) return false;
            latitude += lat;
            longitude += lon;
            if (latitude is < -9_000_000 or > 9_000_000 || longitude is < -18_000_000 or > 18_000_000) return false;
            points++;
        }
        return points >= 2;
    }

    private static bool ReadDelta(string encoded, ref int index, out long delta)
    {
        delta = 0;
        long value = 0;
        for (var shift = 0; index < encoded.Length && shift <= 30; shift += 5)
        {
            var part = encoded[index++] - 63;
            if (part is < 0 or > 63) return false;
            value |= (long)(part & 31) << shift;
            if ((part & 32) != 0) continue;
            delta = (value & 1) == 0 ? value >> 1 : ~(value >> 1);
            return true;
        }
        return false;
    }
}
